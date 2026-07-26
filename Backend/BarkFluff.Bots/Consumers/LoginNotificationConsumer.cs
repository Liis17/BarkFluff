using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Consumers;

/// <summary>
/// Вторая очередь EmailNotification (email-notifications-bots-handler, fanout от Identity).
/// Фильтрует SuccessfulLogin и шлёт DM от login-notifier-бота.
/// </summary>
public class LoginNotificationConsumer : IConsumer<EmailNotification>
{
    private readonly BotRegistryCache _registryCache;
    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<LoginNotificationConsumer> _logger;

    public LoginNotificationConsumer(
        BotRegistryCache registryCache,
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        MetricsCollector metrics,
        ILogger<LoginNotificationConsumer> logger)
    {
        _registryCache = registryCache;
        _messagesClient = messagesClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EmailNotification> context)
    {
        var notification = context.Message;

        if (notification.Type != NotificationType.SuccessfulLogin || notification.OwnerId is not { } userId)
            return;

        if (_registryCache.IsBot(userId))
            return;

        var notifierBot = _registryCache.GetBySystemRole(SystemBotRole.LoginNotifier);
        if (notifierBot is null)
        {
            _logger.LogWarning("Login-notifier бот не найден в реестре — уведомление пропущено");
            return;
        }

        var payload = notification.Payload ?? new Dictionary<string, string>();
        var text = "Выполнен вход в твой аккаунт.\n\n" +
                   $"Устройство: {payload.GetValueOrDefault("devicename", "неизвестно")}\n" +
                   $"ОС: {payload.GetValueOrDefault("os", "неизвестно")}\n" +
                   $"Приложение: {payload.GetValueOrDefault("appname", "неизвестно")}\n" +
                   $"IP: {payload.GetValueOrDefault("ip", "неизвестно")}\n" +
                   $"Местоположение: {payload.GetValueOrDefault("location", "неизвестно")}\n" +
                   $"Время (UTC): {payload.GetValueOrDefault("datetime", "неизвестно")}\n\n" +
                   "Если это не ты — смени пароль и заверши другие сессии.";

        try
        {
            await _messagesClient.SendMessageServerAsync(new MessagesProto.SendMessageServerRequest
            {
                SenderUserId = notifierBot.Id,
                UserId = userId,
                AllowChatCreation = true, // системный бот: чат с пользователем создаётся при первом уведомлении
                Message = new MessagesProto.OutgoingMessage { Text = text },
            });

            _metrics.Increment("login_notifications_sent");
        }
        catch (RpcException ex)
        {
            // Уведомление не критично — не роняем очередь
            _metrics.Increment("login_notifications_errors");
            _logger.LogWarning(ex, "Не удалось отправить login-уведомление пользователю {UserId}", userId);
        }
    }
}
