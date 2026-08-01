using FirebaseAdmin;
using FirebaseAdmin.Messaging;

using Google.Apis.Auth.OAuth2;

using BarkFluff.GrpcServer.Metrics;

using System.Text.Json;

namespace Barkfluff.CloudMessaging.Services;

/// <summary>
/// Сервис для отправки push-уведомлений через Firebase Cloud Messaging.
/// </summary>
public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly FirebaseMessaging? _messaging;
    private readonly IDismissPushSender? _dismissPushSender;
    private readonly MetricsCollector? _metrics;

    public FirebaseService(ILogger<FirebaseService> logger, IConfiguration configuration)
        : this(logger, configuration, null, null)
    {
    }

    public FirebaseService(ILogger<FirebaseService> logger, IConfiguration configuration, MetricsCollector metrics)
        : this(logger, configuration, null, metrics)
    {
    }

    public FirebaseService(
        ILogger<FirebaseService> logger,
        IConfiguration configuration,
        IDismissPushSender? dismissPushSender,
        MetricsCollector? metrics = null)
    {
        _logger = logger;
        _dismissPushSender = dismissPushSender;
        _metrics = metrics;

        if (_dismissPushSender != null)
            return;

        try
        {
            var projectId = configuration["Firebase:ProjectId"];
            var privateKeyId = configuration["Firebase:PrivateKeyId"];
            var privateKey = configuration["Firebase:PrivateKey"];
            var clientEmail = configuration["Firebase:ClientEmail"];
            var clientId = configuration["Firebase:ClientId"];

            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(privateKey) || string.IsNullOrEmpty(clientEmail))
            {
                _logger.LogWarning("Firebase credentials not configured (Firebase:ProjectId, Firebase:PrivateKey, Firebase:ClientEmail). Push notifications will not be sent.");
                return;
            }

            var serviceAccountJson = JsonSerializer.Serialize(new
            {
                type = "service_account",
                project_id = projectId,
                private_key_id = privateKeyId ?? "",
                private_key = privateKey,
                client_email = clientEmail,
                client_id = clientId ?? "",
                auth_uri = "https://accounts.google.com/o/oauth2/auth",
                token_uri = "https://oauth2.googleapis.com/token",
                auth_provider_x509_cert_url = "https://www.googleapis.com/oauth2/v1/certs",
                client_x509_cert_url = $"https://www.googleapis.com/robot/v1/metadata/x509/{Uri.EscapeDataString(clientEmail)}"
            });

            var credential = GoogleCredential.FromJson(serviceAccountJson);

            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = credential
                });
            }

            _messaging = FirebaseMessaging.DefaultInstance;
            _dismissPushSender = new FirebaseDismissPushSender(_messaging);
            _logger.LogInformation("Firebase Admin SDK initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase Admin SDK");
        }
    }

    /// <summary>
    /// Отправляет push-уведомление батчем на список FCM-токенов одним запросом к FCM (до 500 токенов).
    /// </summary>
    public virtual async Task SendNotificationBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string senderName,
        string messagePreview,
        string chatId,
        long senderId,
        long messageId,
        string? avatarUrl,
        string? chatTitle,
        string? chatAvatarUrl,
        bool isGroupChat,
        int contentType,
        string? imagePreviewUrl,
        int attachmentCount,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping notification");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        _metrics?.Increment("push_jobs_received");
        _metrics?.Add("push_target_devices", fcmTokens.Count);

        // Data-only сообщение: без блока Notification, чтобы onMessageReceived
        // всегда вызывался (и в foreground, и в background).
        var multicastMessage = new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["sender_id"] = senderId.ToString(),
                ["type"] = "new_message",
                ["sender_name"] = senderName,
                ["avatar_url"] = avatarUrl ?? "",
                ["chat_title"] = chatTitle ?? "",
                ["chat_avatar_url"] = chatAvatarUrl ?? "",
                ["is_group_chat"] = isGroupChat.ToString().ToLowerInvariant(),
                ["content_type"] = contentType.ToString(),
                ["image_url"] = imagePreviewUrl ?? "",
                ["message_id"] = messageId.ToString(),
                ["message_text"] = TruncateMessage(messagePreview, 100),
                ["attachment_count"] = attachmentCount.ToString()
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        };

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(multicastMessage, cancellationToken);
            _metrics?.Add("fcm_pushes_sent", response.SuccessCount);
            _metrics?.Add("fcm_pushes_failed", response.FailureCount);

            _logger.LogInformation(
                "Push-уведомления отправлены батчем. Success: {Success}, Failure: {Failure}, Total: {Total}",
                response.SuccessCount,
                response.FailureCount,
                fcmTokens.Count);

            if (response.FailureCount > 0)
            {
                var unregisteredTokens = new List<string>();
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    var resp = response.Responses[i];
                    if (resp.IsSuccess)
                        continue;

                    var token = fcmTokens[i];
                    var ex = resp.Exception;

                    if (ex?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                    {
                        unregisteredTokens.Add(token);
                    }
                    else
                    {
                        _logger.LogError(
                            ex,
                            "Ошибка отправки push-уведомления. Token: {TokenPrefix}...",
                            token[..Math.Min(10, token.Length)]);
                    }
                }

                if (unregisteredTokens.Count > 0)
                {
                    _logger.LogWarning(
                        "Невалидные FCM-токены ({Count}): требуется очистка в БД",
                        unregisteredTokens.Count);
                    // TODO: отправить событие на удаление токенов из БД
                }
            }
        }
        catch (Exception ex)
        {
            _metrics?.Add("fcm_pushes_failed", fcmTokens.Count);
            _logger.LogError(ex, "Неожиданная ошибка при батч-отправке push-уведомлений");
        }
    }

    /// <summary>
    /// Отправляет web-получателям payload без содержимого сообщения и вложений.
    /// </summary>
    public virtual Task SendWebNotificationBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string senderName,
        string chatId,
        long senderId,
        long messageId,
        string? avatarUrl,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "new_message",
            ["chat_id"] = chatId,
            ["sender_id"] = senderId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["sender_name"] = senderName,
            ["avatar_url"] = avatarUrl ?? ""
        }, cancellationToken);

    /// <summary>
    /// Отправляет data-only команду на удаление нотификации чата на всех указанных FCM-токенах.
    /// Клиент по type="dismiss_chat_notifications" вызывает NotificationManager.cancel.
    /// </summary>
    public virtual async Task SendDismissBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        if (_dismissPushSender == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping dismiss");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        try
        {
            var responses = await _dismissPushSender.SendAsync(fcmTokens, chatId, cancellationToken);
            var successCount = responses.Count(response => response.IsSuccess);
            var failureCount = responses.Count - successCount;

            _logger.LogInformation(
                "Dismiss push отправлен. ChatId: {ChatId}, Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                chatId,
                fcmTokens.Count,
                successCount,
                failureCount);

            if (failureCount > 0)
            {
                var unregisteredTokens = new List<string>();
                var quotaExceededCount = 0;
                for (var i = 0; i < responses.Count; i++)
                {
                    var resp = responses[i];
                    if (resp.IsSuccess)
                        continue;

                    var token = fcmTokens[i];

                    if (resp.ErrorCode == MessagingErrorCode.Unregistered)
                    {
                        unregisteredTokens.Add(token);
                    }
                    else if (resp.ErrorCode == MessagingErrorCode.QuotaExceeded)
                    {
                        quotaExceededCount++;
                    }
                    else
                    {
                        _logger.LogError(
                            resp.Exception,
                            "Ошибка отправки dismiss push. Token: {TokenPrefix}...",
                            token[..Math.Min(10, token.Length)]);
                    }
                }

                if (unregisteredTokens.Count > 0)
                {
                    _logger.LogWarning(
                        "Невалидные FCM-токены при dismiss ({Count}): требуется очистка в БД",
                        unregisteredTokens.Count);
                }

                if (quotaExceededCount > 0)
                {
                    _logger.LogWarning(
                        "Превышена квота FCM для dismiss push. ChatId: {ChatId}, Tokens: {Count}. Немедленный retry не выполняется",
                        chatId,
                        quotaExceededCount);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при батч-отправке dismiss push");
        }
    }

    /// <summary>
    /// Отправляет data-only high-priority push входящего звонка (type=incoming_call).
    /// Клиент по этому payload показывает экран/нотификацию входящего звонка.
    /// </summary>
    public virtual async Task SendIncomingCallBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string callId,
        long callerUserId,
        string chatId,
        int mediaType,
        long startedAtUnix,
        string callerName,
        string? avatarUrl,
        string? chatTitle,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping incoming_call");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        var multicastMessage = new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["type"] = "incoming_call",
                ["call_id"] = callId,
                ["caller_user_id"] = callerUserId.ToString(),
                ["chat_id"] = chatId,
                ["media_type"] = mediaType.ToString(),
                ["started_at"] = startedAtUnix.ToString(),
                ["caller_name"] = callerName,
                ["avatar_url"] = avatarUrl ?? "",
                ["chat_title"] = chatTitle ?? ""
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        };

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(multicastMessage, cancellationToken);
            _logger.LogInformation(
                "incoming_call push отправлен. CallId: {CallId}, Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                callId, fcmTokens.Count, response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке incoming_call push. CallId: {CallId}", callId);
        }
    }

    public virtual Task SendWebIncomingCallBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string callId,
        long callerUserId,
        string chatId,
        string callerName,
        string? avatarUrl,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "incoming_call",
            ["call_id"] = callId,
            ["caller_user_id"] = callerUserId.ToString(),
            ["chat_id"] = chatId,
            ["caller_name"] = callerName,
            ["avatar_url"] = avatarUrl ?? ""
        }, cancellationToken);

    /// <summary>
    /// Отправляет data-only уведомление о запросе на приватный чат (type=private_chat_invite).
    /// Текст локализуется на клиенте, сервер строк не шлёт.
    /// </summary>
    public virtual async Task SendPrivateChatInviteBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        long inviterUserId,
        string inviterName,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping private_chat_invite");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        var multicastMessage = new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["type"] = "private_chat_invite",
                ["chat_id"] = chatId,
                ["inviter_user_id"] = inviterUserId.ToString(),
                ["inviter_name"] = inviterName,
                ["avatar_url"] = avatarUrl ?? ""
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        };

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(multicastMessage, cancellationToken);
            _logger.LogInformation(
                "private_chat_invite push отправлен. ChatId: {ChatId}, Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                chatId, fcmTokens.Count, response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке private_chat_invite push. ChatId: {ChatId}", chatId);
        }
    }

    public virtual Task SendWebPrivateChatInviteBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        long inviterUserId,
        string inviterName,
        string? avatarUrl,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "private_chat_invite",
            ["chat_id"] = chatId,
            ["inviter_user_id"] = inviterUserId.ToString(),
            ["inviter_name"] = inviterName,
            ["avatar_url"] = avatarUrl ?? ""
        }, cancellationToken);

    /// <summary>
    /// Отправляет data-only команду погасить нотификацию входящего звонка (type=dismiss_call).
    /// </summary>
    public virtual async Task SendCallDismissBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string callId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping dismiss_call");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        var multicastMessage = new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["type"] = "dismiss_call",
                ["call_id"] = callId,
                ["reason"] = reason
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        };

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(multicastMessage, cancellationToken);
            _logger.LogInformation(
                "dismiss_call push отправлен. CallId: {CallId}, Reason: {Reason}, Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                callId, reason, fcmTokens.Count, response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке dismiss_call push. CallId: {CallId}", callId);
        }
    }

    public virtual Task SendWebCallDismissBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string callId,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "dismiss_call",
            ["call_id"] = callId
        }, cancellationToken);

    public virtual Task SendWebDismissBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "dismiss_chat_notifications",
            ["chat_id"] = chatId
        }, cancellationToken);

    /// <summary>
    /// Отправляет произвольное push-уведомление с native Notification-блоком
    /// (title/body/imageUrl) — Android-система отображает уведомление сама,
    /// без участия клиентского кода. Используется для админ-рассылок.
    /// FCM ограничивает SendEachForMulticast 500 токенами на запрос, поэтому
    /// рассылка чанкуется.
    /// </summary>
    public virtual async Task<(int Success, int Failure)> SendAdminBroadcastBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string title,
        string body,
        string? imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping admin broadcast");
            return (0, 0);
        }

        if (fcmTokens.Count == 0)
            return (0, 0);

        var normalizedImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl;
        var totalSuccess = 0;
        var totalFailure = 0;

        foreach (var chunk in fcmTokens.Chunk(500))
        {
            var multicastMessage = new MulticastMessage
            {
                Tokens = chunk,
                Notification = new Notification
                {
                    Title = title,
                    Body = body,
                    ImageUrl = normalizedImageUrl
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ImageUrl = normalizedImageUrl
                    }
                },
                Data = new Dictionary<string, string>
                {
                    ["type"] = "admin_broadcast"
                }
            };

            try
            {
                var response = await _messaging.SendEachForMulticastAsync(multicastMessage, cancellationToken);
                totalSuccess += response.SuccessCount;
                totalFailure += response.FailureCount;

                if (response.FailureCount > 0)
                {
                    var unregisteredTokens = new List<string>();
                    for (var i = 0; i < response.Responses.Count; i++)
                    {
                        var resp = response.Responses[i];
                        if (resp.IsSuccess)
                            continue;

                        var token = chunk[i];
                        var ex = resp.Exception;

                        if (ex?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                        {
                            unregisteredTokens.Add(token);
                        }
                        else
                        {
                            _logger.LogError(
                                ex,
                                "Ошибка отправки admin-broadcast push. Token: {TokenPrefix}...",
                                token[..Math.Min(10, token.Length)]);
                        }
                    }

                    if (unregisteredTokens.Count > 0)
                    {
                        _logger.LogWarning(
                            "Невалидные FCM-токены при admin-broadcast ({Count}): требуется очистка в БД",
                            unregisteredTokens.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при батч-отправке admin-broadcast push");
                totalFailure += chunk.Length;
            }
        }

        _logger.LogInformation(
            "Admin broadcast отправлен. Title: {Title}, Total: {Total}, Success: {Success}, Failure: {Failure}",
            title,
            fcmTokens.Count,
            totalSuccess,
            totalFailure);

        return (totalSuccess, totalFailure);
    }

    public virtual Task SendWebAdminBroadcastBatchAsync(
        IReadOnlyList<string> fcmTokens,
        CancellationToken cancellationToken = default) =>
        SendWebDataBatchAsync(fcmTokens, new Dictionary<string, string>
        {
            ["type"] = "admin_broadcast"
        }, cancellationToken);

    private async Task SendWebDataBatchAsync(
        IReadOnlyList<string> fcmTokens,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping web push");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(new MulticastMessage
            {
                Tokens = [.. fcmTokens],
                Data = data
            }, cancellationToken);

            _metrics?.Add("web_pushes_sent", response.SuccessCount);
            _metrics?.Add("web_pushes_failed", response.FailureCount);
            _logger.LogInformation(
                "Web push {Type} отправлен. Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                data["type"], fcmTokens.Count, response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _metrics?.Add("web_pushes_failed", fcmTokens.Count);
            _logger.LogError(ex, "Неожиданная ошибка при отправке web push {Type}", data["type"]);
        }
    }

    private static string TruncateMessage(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
