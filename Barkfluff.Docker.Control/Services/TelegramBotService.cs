using Barkfluff.Docker.Control.Models;

using Microsoft.Extensions.Options;

using System.Text;

using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Barkfluff.Docker.Control.Services;

public class TelegramBotService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IOptions<TelegramSettings> _settings;
    private readonly PendingAuthService _pendingAuthService;
    private readonly TokenService _tokenService;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly CancellationTokenSource _cts = new();

    public TelegramBotService(
        IOptions<TelegramSettings> settings,
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        ILogger<TelegramBotService> logger)
    {
        _settings = settings;
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(settings.Value.BotToken))
        {
            throw new InvalidOperationException(
                "Telegram bot token is not configured. Please set 'Telegram:BotToken' in appsettings.json or via environment variable 'Telegram__BotToken'.");
        }

        _botClient = new TelegramBotClient(settings.Value.BotToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Telegram Bot Service...");

        // Create the update handler
        var updateHandler = new UpdateHandler(
            _settings,
            _pendingAuthService,
            _tokenService,
            _logger);

        // Start receiving updates
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _ = _botClient.ReceiveAsync(
            updateHandler: updateHandler,
            receiverOptions: receiverOptions,
            cancellationToken: _cts.Token);

        // Verify bot connection
        try
        {
            var me = await _botClient.GetMe(cancellationToken);
            _logger.LogInformation("Telegram bot connected: @{BotUsername}", me.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Telegram bot. Check your BotToken.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Telegram Bot Service...");
        _cts.Cancel();
        await Task.CompletedTask;
    }

    public async Task SendAuthRequestAsync(PendingAuthRequest request)
    {
        if (_settings.Value.AdminUserIds.Count == 0)
        {
            throw new InvalidOperationException("No admin user IDs configured.");
        }

        var message = new StringBuilder();
        message.AppendLine("🔐 *New Authorization Request*");
        message.AppendLine();
        message.AppendLine($"IP: `{request.IpAddress ?? "Unknown"}`");
        message.AppendLine($"Browser: {request.Browser ?? "Unknown"}");
        message.AppendLine($"OS: {request.Os ?? "Unknown"}");
        message.AppendLine($"User Agent: `{(request.UserAgent?.Length > 50 ? request.UserAgent[..50] + "..." : request.UserAgent ?? "Unknown")}`");
        message.AppendLine($"Request ID: `{request.RequestId}`");
        message.AppendLine($"Time: {request.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Approve", $"auth:{request.RequestId}:approve"),
                InlineKeyboardButton.WithCallbackData("❌ Reject", $"auth:{request.RequestId}:reject")
            }
        });

        foreach (var adminId in _settings.Value.AdminUserIds)
        {
            try
            {
                var msg = await _botClient.SendMessage(
                    adminId,
                    message.ToString(),
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard);

                _pendingAuthService.SetTelegramMessageId(request.RequestId, msg.MessageId);
                _logger.LogInformation("Auth request sent to Telegram admin {AdminId}", adminId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send auth request to admin {AdminId}", adminId);
            }
        }
    }
}

// Update Handler class
public class UpdateHandler : IUpdateHandler
{
    private readonly IOptions<TelegramSettings> _settings;
    private readonly PendingAuthService _pendingAuthService;
    private readonly TokenService _tokenService;
    private readonly ILogger _logger;

    public UpdateHandler(
        IOptions<TelegramSettings> settings,
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        ILogger logger)
    {
        _settings = settings;
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _logger = logger;
    }

    public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.CallbackQuery)
            {
                return HandleCallbackQueryAsync(botClient, update.CallbackQuery!, cancellationToken);
            }
            else if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Text)
            {
                return HandleMessageAsync(botClient, update.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }

        return Task.CompletedTask;
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var userId = callbackQuery.From.Id;
        if (!_settings.Value.AdminUserIds.Contains(userId))
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "You are not authorized to perform this action.",
                showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data)) return;

        // Parse callback data: "auth:requestId:action"
        var parts = data.Split(':');
        if (parts.Length >= 3 && parts[0] == "auth")
        {
            var requestId = parts[1];
            var action = parts[2];

            var request = _pendingAuthService.GetRequest(requestId);
            if (request == null || request.Status != AuthRequestStatus.Pending)
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "This request has expired or already processed.",
                    cancellationToken: cancellationToken);
                return;
            }

            if (action == "approve")
            {
                var tokenId = _tokenService.CreateToken(
                    request.IpAddress,
                    request.UserAgent,
                    request.TokenName ?? "Web Session");

                _pendingAuthService.UpdateRequestStatus(
                    requestId,
                    AuthRequestStatus.Approved,
                    tokenId,
                    userId);

                await botClient.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    $"✅ *Authorization Approved*\n\nToken: `{tokenId}`\n\nIP: {request.IpAddress ?? "Unknown"}\nBrowser: {request.Browser ?? "Unknown"}\nOS: {request.Os ?? "Unknown"}",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Authorization approved!",
                    cancellationToken: cancellationToken);
            }
            else if (action == "reject")
            {
                _pendingAuthService.UpdateRequestStatus(
                    requestId,
                    AuthRequestStatus.Rejected,
                    approvedBy: userId);

                await botClient.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    $"❌ *Authorization Rejected*\n\nIP: {request.IpAddress ?? "Unknown"}\nBrowser: {request.Browser ?? "Unknown"}\nOS: {request.Os ?? "Unknown"}",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Authorization rejected.",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        if (!_settings.Value.AdminUserIds.Contains(userId))
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "You are not authorized to use this bot.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = message.Text!;
        var args = text.Split(' ');

        switch (args[0])
        {
            case "/start":
                await botClient.SendMessage(
                    message.Chat.Id,
                    "*Docker Control Bot*\n\n" +
                    "Commands:\n" +
                    "/tokens - List all active tokens\n" +
                    "/kill `<guid>` - Revoke a token\n" +
                    "/rename `<guid>` `<name>` - Rename a token\n" +
                    "/pending - Show pending auth requests",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "/tokens":
                var tokens = _tokenService.GetAllTokens();
                var sb = new StringBuilder();
                sb.AppendLine("*Active Tokens:*\n");

                if (tokens.Count == 0)
                {
                    sb.AppendLine("No active tokens.");
                }
                else
                {
                    foreach (var token in tokens.Take(20))
                    {
                        var isExpired = token.IsExpired(3);
                        sb.AppendLine($"`{token.Id}` {(isExpired ? "⚠️ " : "")}");
                        sb.AppendLine($"  Name: {token.Name}");
                        sb.AppendLine($"  Created: {token.CreatedAt:yyyy-MM-dd HH:mm}");
                        sb.AppendLine($"  Last Activity: {token.LastActivity:yyyy-MM-dd HH:mm}");
                        sb.AppendLine($"  IP: {token.IpAddress ?? "Unknown"}");
                        sb.AppendLine();
                    }

                    if (tokens.Count > 20)
                    {
                        sb.AppendLine($"... and {tokens.Count - 20} more.");
                    }
                }

                await botClient.SendMessage(
                    message.Chat.Id,
                    sb.ToString(),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "/kill":
                if (args.Length < 2)
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "Usage: /kill `<guid>`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else if (Guid.TryParse(args[1], out var tokenId))
                {
                    if (_tokenService.DeleteToken(tokenId))
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"✅ Token `{tokenId}` revoked.",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"❌ Token `{tokenId}` not found.",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "Invalid GUID format.",
                        cancellationToken: cancellationToken);
                }
                break;

            case "/rename":
                if (args.Length < 3)
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "Usage: /rename `<guid>` `<new name>`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else if (Guid.TryParse(args[1], out var renameTokenId))
                {
                    var newName = string.Join(" ", args.Skip(2));
                    if (_tokenService.RenameToken(renameTokenId, newName))
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"✅ Token renamed to: {newName}",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"❌ Token not found.",
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "Invalid GUID format.",
                        cancellationToken: cancellationToken);
                }
                break;

            case "/pending":
                var pending = _pendingAuthService.GetAllPending();
                var psb = new StringBuilder();
                psb.AppendLine("*Pending Auth Requests:*\n");

                if (pending.Count == 0)
                {
                    psb.AppendLine("No pending requests.");
                }
                else
                {
                    foreach (var req in pending)
                    {
                        var age = (DateTime.UtcNow - req.CreatedAt).TotalMinutes;
                        psb.AppendLine($"`{req.RequestId}`");
                        psb.AppendLine($"  IP: {req.IpAddress ?? "Unknown"}");
                        psb.AppendLine($"  Browser: {req.Browser ?? "Unknown"}");
                        psb.AppendLine($"  Age: {age:F1} minutes");
                        psb.AppendLine();
                    }
                }

                await botClient.SendMessage(
                    message.Chat.Id,
                    psb.ToString(),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            default:
                await botClient.SendMessage(
                    message.Chat.Id,
                    "Unknown command. Use /start to see available commands.",
                    cancellationToken: cancellationToken);
                break;
        }
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram error from {Source}", source);
        return Task.CompletedTask;
    }
}
