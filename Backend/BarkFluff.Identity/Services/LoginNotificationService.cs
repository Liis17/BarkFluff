using System.Globalization;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using Grpc.Core;

namespace BarkFluff.Identity.Services;

public sealed class LoginNotificationService(
    AuthPropertiesStorage properties,
    UsersServerApi.UsersServerApiClient users,
    NotificationQueueSender notifications,
    ITelegramAuthBot telegramBot,
    TelegramAuthOptions telegram,
    IConfiguration configuration,
    ILogger<LoginNotificationService> logger)
{
    public async Task SendAsync(
        long userId,
        NotificationType type,
        string username,
        string ipAddress,
        string deviceName,
        string operationSystem,
        string appName,
        string location,
        DateTime dateTimeUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await properties.GetUserAuthProperties(userId);
            var contact = await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = userId }, cancellationToken: cancellationToken);
            var payload = new Dictionary<string, string>
            {
                ["username"] = username,
                ["ip"] = ipAddress,
                ["devicename"] = deviceName,
                ["os"] = operationSystem,
                ["location"] = location,
                ["appname"] = appName,
                ["datetime"] = dateTimeUtc.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            };

            var email = contact.Contact?.Email;
            var emailAvailable = configuration.GetValue("Email:Enabled", true) && !string.IsNullOrWhiteSpace(email);
            var telegramAvailable = settings?.TelegramId.HasValue == true && settings.TelegramEnabled && telegram.Configured;
            var channel = settings?.NotificationChannel ?? LoginNotificationChannel.Email;
            if ((channel == LoginNotificationChannel.Telegram && !telegramAvailable && emailAvailable) ||
                (channel == LoginNotificationChannel.Email && !emailAvailable && telegramAvailable))
                channel = emailAvailable ? LoginNotificationChannel.Email : LoginNotificationChannel.Telegram;

            if (channel == LoginNotificationChannel.Telegram)
            {
                if (!telegramAvailable || settings?.TelegramId is not { } telegramId)
                {
                    logger.LogWarning("Telegram login notification unavailable for user {UserId}", userId);
                    return;
                }

                await telegramBot.Send(telegramId, FormatTelegramMessage(type, payload), null, cancellationToken);
                return;
            }

            if (channel != LoginNotificationChannel.Email || !emailAvailable)
            {
                logger.LogWarning("Email login notification unavailable for user {UserId}", userId);
                return;
            }

            await notifications.SendNotification(new EmailNotification
            {
                OwnerId = userId,
                Address = email,
                CreatedAt = dateTimeUtc,
                Payload = payload,
                ServiceId = ServiceId.Identity,
                Title = type == NotificationType.FailedLogin
                    ? "Неуспешная попытка входа в аккаунт"
                    : "Успешный вход в аккаунт",
                Type = type
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Не удалось отправить уведомление о входе для пользователя {UserId}", userId);
        }
    }

    private string FormatTelegramMessage(NotificationType type, IReadOnlyDictionary<string, string> payload)
    {
        var failed = type == NotificationType.FailedLogin;
        return $"🔐 BarkFluff · {telegram.NodeName}\n\n" +
               $"👤 Аккаунт: {payload["username"]}\n" +
               $"🧭 Событие: {(failed ? "Неудачная попытка входа" : "Вход в аккаунт")}\n" +
               $"💻 Устройство: {payload["devicename"]} · {payload["os"]}\n" +
               $"📱 Приложение: {payload["appname"]}\n" +
               $"🌐 IP: {payload["ip"]}\n" +
               $"📍 Местоположение: {payload["location"]}\n" +
               $"🕒 Время: {payload["datetime"]} UTC\n\n" +
               (failed
                   ? "⚠️ Если это были не вы, смените пароль и проверьте активные сессии."
                   : "⚠️ Если это были не вы, смените пароль и завершите другие сессии.");
    }
}
