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
    /// Отправляет push-уведомление на указанное устройство.
    /// </summary>
    public async Task SendNotificationAsync(
        string fcmToken,
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
        int attachmentCount)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping notification");
            return;
        }

        try
        {
            // Data-only сообщение: без блока Notification, чтобы onMessageReceived
            // всегда вызывался (и в foreground, и в background).
            // Это позволяет Android-клиенту самому формировать уведомление
            // с аватаром, эмодзи-форматированием и правильным PendingIntent.
            var message = new Message
            {
                Token = fcmToken,
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

            var resultMessageId = await _messaging.SendAsync(message);

            _logger.LogInformation(
                "Push-уведомление отправлено. MessageId: {MessageId}, Token: {TokenPrefix}...",
                resultMessageId,
                fcmToken[..Math.Min(10, fcmToken.Length)]);
        }
        catch (FirebaseMessagingException ex)
        {
            if (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                _logger.LogWarning(
                    "FCM токен невалиден или истёк: {TokenPrefix}...",
                    fcmToken[..Math.Min(10, fcmToken.Length)]);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Ошибка отправки push-уведомления. Token: {TokenPrefix}...",
                    fcmToken[..Math.Min(10, fcmToken.Length)]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке push-уведомления");
        }
    }

    private static string TruncateMessage(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
