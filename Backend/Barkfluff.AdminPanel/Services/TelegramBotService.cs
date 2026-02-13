using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

using System.Text;

using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Barkfluff.AdminPanel.Services;

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

        // Validate that admins are configured
        if (_settings.Value.ParsedAdmins.Count == 0)
        {
            throw new InvalidOperationException("No admins configured. Please set 'Telegram:Admins' with format 'userId1:username1,userId2:username2'");
        }

        var updateHandler = new UpdateHandler(
            _botClient,
            _settings,
            _pendingAuthService,
            _tokenService,
            _logger);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _ = _botClient.ReceiveAsync(
            updateHandler: updateHandler,
            receiverOptions: receiverOptions,
            cancellationToken: _cts.Token);

        try
        {
            var me = await _botClient.GetMe(cancellationToken);
            _logger.LogInformation("Telegram bot connected: @{BotUsername}", me.Username);
            _logger.LogInformation("Configured admins: {AdminCount}", _settings.Value.ParsedAdmins.Count);
            foreach (var admin in _settings.Value.ParsedAdmins)
            {
                _logger.LogInformation("  - {Username} (ID: {TelegramUserId})", admin.Username, admin.TelegramUserId);
            }
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

    /// <summary>
    /// Result of checking if the bot can reach a user
    /// </summary>
    public class BotReachabilityResult
    {
        public bool CanSend { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public static BotReachabilityResult Success() => new() { CanSend = true };

        public static BotReachabilityResult Blocked(string message) => new()
        {
            CanSend = false,
            ErrorCode = "bot_blocked",
            ErrorMessage = message
        };

        public static BotReachabilityResult NotFound(string message) => new()
        {
            CanSend = false,
            ErrorCode = "bot_not_started",
            ErrorMessage = message
        };

        public static BotReachabilityResult UnknownError(string message) => new()
        {
            CanSend = false,
            ErrorCode = "unknown_error",
            ErrorMessage = message
        };
    }

    /// <summary>
    /// Checks if the bot can send messages to a specific user
    /// </summary>
    public async Task<BotReachabilityResult> CheckBotReachabilityAsync(long userId)
    {
        try
        {
            // SendChatAction is a lightweight way to check if we can reach the user
            await _botClient.SendChatAction(userId, ChatAction.Typing);

            return BotReachabilityResult.Success();
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User blocked the bot
            return BotReachabilityResult.Blocked("Бот заблокирован пользователем");
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            // Chat not found - user hasn't started the bot
            return BotReachabilityResult.NotFound("Начните диалог с ботом");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking bot reachability for user {UserId}", userId);
            return BotReachabilityResult.UnknownError($"Ошибка проверки: {ex.Message}");
        }
    }

    public async Task SendAuthRequestAsync(PendingAuthRequest request)
    {
        if (request.TargetTelegramUserId.HasValue)
        {
            // Send only to the target admin
            await SendAuthRequestToAdmin(request, request.TargetTelegramUserId.Value);
        }
        else
        {
            // Legacy behavior: send to all admins
            foreach (var admin in _settings.Value.ParsedAdmins)
            {
                await SendAuthRequestToAdmin(request, admin.TelegramUserId);
            }
        }
    }

    private async Task SendAuthRequestToAdmin(PendingAuthRequest request, long targetUserId)
    {
        var targetAdmin = _settings.Value.GetAdminByTelegramId(targetUserId);
        var nickname = request.Nickname ?? "Unknown";
        var browser = request.Browser ?? "Unknown";
        var os = request.Os ?? "Unknown";
        var time = request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

        // Using HTML format instead of Markdown to avoid parsing issues with emojis
        var message = $"🔐 <b>Запрос на вход в панель</b>\n" +
                      $"\n" +
                      $"👤 Администратор: {nickname}\n" +
                      $"🖥 Устройство: {browser} на {os}\n" +
                      $"🕐 Время: {time} UTC\n" +
                      $"\n" +
                      $"Подтвердите или отклоните вход.";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Разрешить", $"auth:{request.RequestId}:approve"),
                InlineKeyboardButton.WithCallbackData("❌ Отклонить", $"auth:{request.RequestId}:reject")
            }
        });

        try
        {
            var msg = await _botClient.SendMessage(
                targetUserId,
                message,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard);

            _pendingAuthService.SetTelegramMessageId(request.RequestId, msg.MessageId);
            _logger.LogInformation("Auth request sent to Telegram admin {AdminId} ({Username})", targetUserId, targetAdmin?.Username ?? "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send auth request to admin {AdminId}", targetUserId);
        }
    }
}

// Update Handler class
public class UpdateHandler : IUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IOptions<TelegramSettings> _settings;
    private readonly PendingAuthService _pendingAuthService;
    private readonly TokenService _tokenService;
    private readonly ILogger _logger;

    public UpdateHandler(
        ITelegramBotClient botClient,
        IOptions<TelegramSettings> settings,
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        ILogger logger)
    {
        _botClient = botClient;
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
        if (!_settings.Value.IsAdmin(userId))
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

            // Verify that this admin is the target admin (if targeted)
            if (request.TargetTelegramUserId.HasValue && request.TargetTelegramUserId.Value != userId)
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Этот запрос адресован другому администратору.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            var nickname = request.Nickname ?? "Unknown";
            var browser = request.Browser ?? "Unknown";
            var os = request.Os ?? "Unknown";
            var time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var adminUsername = _settings.Value.GetUsername(userId) ?? "Unknown";

            if (action == "approve")
            {
                var tokenId = _tokenService.CreateToken(
                    request.IpAddress,
                    request.UserAgent,
                    request.TokenName ?? "Web Session",
                    adminUsername,
                    userId);

                _pendingAuthService.UpdateRequestStatus(
                    requestId,
                    AuthRequestStatus.Approved,
                    tokenId,
                    userId);

                // Update original message
                var approvedMsg = $"✅ <b>Вход выполнен</b>\n\n" +
                                  $"👤 {nickname} вошёл в панель управления\n" +
                                  $"🖥 {browser} на {os}\n" +
                                  $"🕐 {time} UTC";

                await botClient.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    approvedMsg,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);

                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Вход разрешён!",
                    cancellationToken: cancellationToken);
            }
            else if (action == "reject")
            {
                _pendingAuthService.UpdateRequestStatus(
                    requestId,
                    AuthRequestStatus.Rejected,
                    approvedBy: userId);

                var rejectedMsg = $"❌ <b>Вход отклонён</b>\n\n" +
                                  $"👤 {nickname} — запрос отклонён\n" +
                                  $"🕐 {time} UTC";

                await botClient.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    rejectedMsg,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);

                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Вход отклонён.",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        if (!_settings.Value.IsAdmin(userId))
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
                    "*BarkFluff Admin Panel Bot*\n\n" +
                    "Commands:\n" +
                    "/tokens - List your active tokens\n" +
                    "/kill `<guid>` - Revoke a token\n" +
                    "/rename `<guid>` `<name>` - Rename a token\n" +
                    "/pending - Show your pending auth requests",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "/tokens":
                var tokens = _tokenService.GetTokensByAdmin(userId);
                var sb = new StringBuilder();
                sb.AppendLine("*Your Active Tokens:*\n");

                if (tokens.Count == 0)
                {
                    sb.AppendLine("No active tokens.");
                }
                else
                {
                    foreach (var token in tokens.Take(20))
                    {
                        var isExpired = token.IsExpired(3);
                        sb.AppendLine($"`{token.Id}` {(isExpired ? "\u26a0\ufe0f " : "")}");
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
                    if (_tokenService.DeleteTokenByAdmin(tokenId, userId))
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"\u2705 Token `{tokenId}` revoked.",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"\u274c Token `{tokenId}` not found or doesn't belong to you.",
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
                    if (_tokenService.RenameTokenByAdmin(renameTokenId, newName, userId))
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"\u2705 Token renamed to: {newName}",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"\u274c Token not found or doesn't belong to you.",
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
                var pending = _pendingAuthService.GetPendingByAdmin(userId);
                var psb = new StringBuilder();
                psb.AppendLine("*Your Pending Auth Requests:*\n");

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
                        psb.AppendLine($"  Nickname: {req.Nickname ?? "Unknown"}");
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
