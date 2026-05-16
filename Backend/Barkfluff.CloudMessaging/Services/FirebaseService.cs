using FirebaseAdmin;
using FirebaseAdmin.Messaging;

using Google.Apis.Auth.OAuth2;

using System.Text.Json;

namespace Barkfluff.CloudMessaging.Services;

/// <summary>
/// Сервис для отправки push-уведомлений через Firebase Cloud Messaging.
/// </summary>
public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly FirebaseMessaging? _messaging;

    public FirebaseService(ILogger<FirebaseService> logger, IConfiguration configuration)
    {
        _logger = logger;

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
    public async Task SendNotificationBatchAsync(
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
            _logger.LogError(ex, "Неожиданная ошибка при батч-отправке push-уведомлений");
        }
    }

    /// <summary>
    /// Отправляет data-only команду на удаление нотификации чата на всех указанных FCM-токенах.
    /// Клиент по type="dismiss_chat_notifications" вызывает NotificationManager.cancel.
    /// </summary>
    public async Task SendDismissBatchAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping dismiss");
            return;
        }

        if (fcmTokens.Count == 0)
            return;

        var multicastMessage = new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["type"] = "dismiss_chat_notifications",
                ["chat_id"] = chatId
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
                "Dismiss push отправлен. ChatId: {ChatId}, Tokens: {Total}, Success: {Success}, Failed: {Failed}",
                chatId,
                fcmTokens.Count,
                response.SuccessCount,
                response.FailureCount);

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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при батч-отправке dismiss push");
        }
    }

    /// <summary>
    /// Отправляет произвольное push-уведомление с native Notification-блоком
    /// (title/body/imageUrl) — Android-система отображает уведомление сама,
    /// без участия клиентского кода. Используется для админ-рассылок.
    /// FCM ограничивает SendEachForMulticast 500 токенами на запрос, поэтому
    /// рассылка чанкуется.
    /// </summary>
    public async Task<(int Success, int Failure)> SendAdminBroadcastBatchAsync(
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

    private static string TruncateMessage(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
