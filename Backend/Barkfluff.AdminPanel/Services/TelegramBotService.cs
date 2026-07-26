using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

using System.Net;
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
    private readonly IOptions<AuthSettings> _authSettings;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly CancellationTokenSource _cts = new();

    public TelegramBotService(
        IOptions<TelegramSettings> settings,
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        IOptions<AuthSettings> authSettings,
        ILogger<TelegramBotService> logger)
    {
        _settings = settings;
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _authSettings = authSettings;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(settings.Value.BotToken))
        {
            throw new InvalidOperationException(
                "Telegram bot token is not configured. Please set 'Telegram:BotToken' in appsettings.json or via environment variable 'Telegram__BotToken'.");
        }

        _botClient = CreateBotClient(settings.Value, logger);
    }

    private static ITelegramBotClient CreateBotClient(TelegramSettings settings, ILogger logger)
    {
        if (!TryGetProxyUri(settings.Proxy.Url, out var proxyUri))
        {
            return new TelegramBotClient(settings.BotToken);
        }

        var proxy = new WebProxy(proxyUri);
        if (!string.IsNullOrWhiteSpace(settings.Proxy.Username))
        {
            proxy.Credentials = new NetworkCredential(settings.Proxy.Username, settings.Proxy.Password);
        }

        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = proxy
        };

        var httpClient = new HttpClient(handler, disposeHandler: true);

        logger.LogInformation("Telegram bot proxy enabled: {Scheme}://{Host}:{Port}", proxyUri.Scheme, proxyUri.Host, proxyUri.Port);

        return new TelegramBotClient(settings.BotToken, httpClient);
    }

    private static bool TryGetProxyUri(string? proxyUrl, out Uri proxyUri)
    {
        proxyUri = default!;

        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            return false;
        }

        var normalizedProxyUrl = proxyUrl.Trim();
        if (normalizedProxyUrl.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedProxyUrl = "socks5://" + normalizedProxyUrl[8..];
        }

        return Uri.TryCreate(normalizedProxyUrl, UriKind.Absolute, out proxyUri);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Telegram Bot Service...");

        if (!string.IsNullOrWhiteSpace(_settings.Value.Proxy.Url) &&
            !TryGetProxyUri(_settings.Value.Proxy.Url, out _))
        {
            _logger.LogWarning("Telegram proxy URL is invalid. The bot will start without a proxy. Configure 'Telegram:Proxy:Url' or 'Telegram__Proxy__Url' as a valid absolute URI.");
        }

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
            _authSettings,
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
        var nickname = WebUtility.HtmlEncode(request.Nickname ?? "Unknown");
        var browser = WebUtility.HtmlEncode(request.Browser ?? "Unknown");
        var os = WebUtility.HtmlEncode(request.Os ?? "Unknown");
        var time = request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var ipAddress = WebUtility.HtmlEncode(request.IpAddress ?? "Unknown");

        // Using HTML format instead of Markdown to avoid parsing issues with emojis
        var message = $"🔐 <b>Запрос на вход в панель</b>\n" +
                      $"\n" +
                      $"👤 Администратор: {nickname}\n" +
                      $"🖥 Устройство: {browser} на {os}\n" +
                      $"🌐 IP: {ipAddress}\n" +
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
    private readonly IOptions<AuthSettings> _authSettings;
    private readonly ILogger _logger;

    public UpdateHandler(
        ITelegramBotClient botClient,
        IOptions<TelegramSettings> settings,
        PendingAuthService pendingAuthService,
        TokenService tokenService,
        IOptions<AuthSettings> authSettings,
        ILogger logger)
    {
        _botClient = botClient;
        _settings = settings;
        _pendingAuthService = pendingAuthService;
        _tokenService = tokenService;
        _authSettings = authSettings;
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

            return;
        }

        if (parts.Length == 3 && parts[0] == "session")
        {
            await HandleSessionCallbackAsync(botClient, callbackQuery, userId, parts[1], parts[2], cancellationToken);
        }
        else if (data == "menu:sessions")
        {
            await SendSessionsAsync(botClient, callbackQuery.Message!.Chat.Id, userId, 0, cancellationToken, callbackQuery.Message.MessageId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }
        else if (data == "menu:home")
        {
            await SendMainMenuAsync(botClient, callbackQuery.Message!.Chat.Id, cancellationToken, callbackQuery.Message.MessageId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
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
                await SendMainMenuAsync(botClient, message.Chat.Id, cancellationToken);
                break;

            case "/tokens":
            case "/sessions":
                await SendSessionsAsync(botClient, message.Chat.Id, userId, 0, cancellationToken);
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

    private async Task HandleSessionCallbackAsync(
        ITelegramBotClient botClient,
        CallbackQuery callbackQuery,
        long userId,
        string action,
        string value,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;

        if (action == "list" && int.TryParse(value, out var page))
        {
            await SendSessionsAsync(botClient, chatId, userId, Math.Max(page, 0), cancellationToken, callbackQuery.Message.MessageId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        if (!Guid.TryParse(value, out var tokenId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Некорректная сессия.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        var token = _tokenService.GetToken(tokenId);
        if (token?.ApprovedByTelegramUserId != userId)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Сессия не найдена или вам не принадлежит.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        if (action == "view")
        {
            await ShowSessionAsync(botClient, chatId, token, cancellationToken, callbackQuery.Message.MessageId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        if (action == "confirm")
        {
            var text = $"⚠️ <b>Завершить сессию?</b>\n\n" +
                       $"🖥 {WebUtility.HtmlEncode(token.Name)}\n" +
                       $"🌐 {WebUtility.HtmlEncode(token.IpAddress ?? "IP неизвестен")}\n\n" +
                       "Браузер с этой сессией сразу потеряет доступ к панели.";
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛑 Да, завершить", $"session:revoke:{token.Id}"),
                    InlineKeyboardButton.WithCallbackData("Назад", $"session:view:{token.Id}")
                }
            });
            await botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        if (action == "revoke")
        {
            _tokenService.DeleteTokenByAdmin(tokenId, userId);
            var text = $"✅ <b>Сессия завершена</b>\n\n" +
                       $"🖥 {WebUtility.HtmlEncode(token.Name)}\n" +
                       "Доступ из этого браузера отозван.";
            var keyboard = new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("← К списку сессий", "session:list:0"));
            await botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Сессия завершена.", cancellationToken: cancellationToken);
        }
    }

    private async Task SendMainMenuAsync(ITelegramBotClient botClient, ChatId chatId, CancellationToken cancellationToken, int? messageId = null)
    {
        const string text = "🛡 <b>BarkFluff Admin Panel</b>\n\n" +
                            "Через бота можно подтверждать вход и управлять своими активными сессиями.";
        var keyboard = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData("🖥 Мои сессии", "menu:sessions"));

        if (messageId.HasValue)
        {
            await botClient.EditMessageText(chatId, messageId.Value, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
    }

    private async Task SendSessionsAsync(ITelegramBotClient botClient, ChatId chatId, long userId, int page, CancellationToken cancellationToken, int? messageId = null)
    {
        const int pageSize = 8;
        var tokens = _tokenService.GetActiveTokensByAdmin(userId);
        var pageCount = Math.Max(1, (int)Math.Ceiling(tokens.Count / (double)pageSize));
        page = Math.Min(page, pageCount - 1);
        var pageTokens = tokens.Skip(page * pageSize).Take(pageSize).ToList();

        var rows = pageTokens
            .Select(token => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🖥 {Truncate(token.Name, 28)} · {FormatDate(token.LastActivity)}",
                    $"session:view:{token.Id}")
            })
            .ToList();

        var navigation = new List<InlineKeyboardButton>();
        if (page > 0)
            navigation.Add(InlineKeyboardButton.WithCallbackData("←", $"session:list:{page - 1}"));
        navigation.Add(InlineKeyboardButton.WithCallbackData("🔄 Обновить", $"session:list:{page}"));
        if (page < pageCount - 1)
            navigation.Add(InlineKeyboardButton.WithCallbackData("→", $"session:list:{page + 1}"));
        rows.Add(navigation.ToArray());
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Меню", "menu:home") });

        var text = tokens.Count == 0
            ? "🖥 <b>Мои сессии</b>\n\nАктивных сессий нет."
            : $"🖥 <b>Мои сессии</b>\n\nАктивно: {tokens.Count}. Выберите сессию, чтобы посмотреть детали или завершить её.\nСтраница {page + 1} из {pageCount}.";
        var keyboard = new InlineKeyboardMarkup(rows);

        if (messageId.HasValue)
        {
            await botClient.EditMessageText(chatId, messageId.Value, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
    }

    private async Task ShowSessionAsync(ITelegramBotClient botClient, ChatId chatId, AuthToken token, CancellationToken cancellationToken, int messageId)
    {
        var expiresAt = token.LastActivity.AddDays(_authSettings.Value.TokenExpirationDays);
        var text = $"🖥 <b>Сессия</b>\n\n" +
                   $"<b>Название:</b> {WebUtility.HtmlEncode(token.Name)}\n" +
                   $"<b>IP:</b> {WebUtility.HtmlEncode(token.IpAddress ?? "Неизвестен")}\n" +
                   $"<b>Создана:</b> {FormatDate(token.CreatedAt)} UTC\n" +
                   $"<b>Активность:</b> {FormatDate(token.LastActivity)} UTC\n" +
                   $"<b>Истечёт:</b> {FormatDate(expiresAt)} UTC";
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🛑 Завершить сессию", $"session:confirm:{token.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("← К списку", "session:list:0") }
        });
        await botClient.EditMessageText(chatId, messageId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    private static string FormatDate(DateTime value) => value.ToString("dd.MM.yyyy HH:mm");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

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
