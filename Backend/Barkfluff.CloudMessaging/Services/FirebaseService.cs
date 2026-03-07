using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace Barkfluff.CloudMessaging.Services;

/// <summary>
/// Сервис для отправки push-уведомлений через Firebase Cloud Messaging.
/// </summary>
public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly FirebaseMessaging? _messaging;

    public FirebaseService(ILogger<FirebaseService> logger)
    {
        _logger = logger;

        // Инициализация Firebase Admin SDK из service account JSON файла
        var credentialPath = "/app/firebase/barkfluff-firebase-adminsdk.json";

        if (!File.Exists(credentialPath))
        {
            _logger.LogWarning("Firebase credentials file not found at {Path}. Push notifications will not be sent.", credentialPath);
            return;
        }

        try
        {
            var credential = GoogleCredential.FromFile(credentialPath);

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
    /// <param name="fcmToken">Firebase токен устройства</param>
    /// <param name="senderName">Имя отправителя сообщения</param>
    /// <param name="messagePreview">Текст сообщения (превью)</param>
    /// <param name="chatId">ID чата</param>
    /// <param name="senderId">ID отправителя</param>
    public async Task SendNotificationAsync(
        string fcmToken,
        string senderName,
        string messagePreview,
        string chatId,
        long senderId)
    {
        if (_messaging == null)
        {
            _logger.LogWarning("Firebase messaging not initialized, skipping notification");
            return;
        }

        try
        {
            var message = new Message
            {
                Token = fcmToken,
                Notification = new Notification
                {
                    Title = senderName,
                    Body = TruncateMessage(messagePreview, 100)
                },
                Data = new Dictionary<string, string>
                {
                    ["chat_id"] = chatId,
                    ["sender_id"] = senderId.ToString(),
                    ["type"] = "new_message"
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        Priority = NotificationPriority.HIGH,
                        Sound = "default"
                    }
                }
            };

            var messageId = await _messaging.SendAsync(message);

            _logger.LogInformation(
                "Push-уведомление отправлено. MessageId: {MessageId}, Token: {TokenPrefix}...",
                messageId,
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
