using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeNewMessages;

using MassTransit;

using MediatR;

namespace BarkFluff.Updates.Features.PushNotifications;

/// <summary>
/// Обработчик новых сообщений, который планирует отправку push-уведомления через 5 секунд.
/// Если сообщение будет прочитано в течение этого времени, push-уведомление отменяется.
/// </summary>
public class PushNotificationSchedulerHandler : INotificationHandler<NewMessageNotification>
{
    private readonly PendingPushTracker _pendingPushTracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushNotificationSchedulerHandler> _logger;

    public PushNotificationSchedulerHandler(
        PendingPushTracker pendingPushTracker,
        IServiceScopeFactory scopeFactory,
        ILogger<PushNotificationSchedulerHandler> logger)
    {
        _pendingPushTracker = pendingPushTracker;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(NewMessageNotification notification, CancellationToken cancellationToken)
    {
        // Получатели - все участники чата кроме отправителя
        var recipients = notification.Members
            .Where(id => id != notification.Message.SenderId)
            .ToList();

        if (recipients.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Планирование push-уведомлений для сообщения {MessageId} в чате {ChatId}. Получателей: {Count}",
            notification.Message.Id,
            notification.ChatId,
            recipients.Count);

        foreach (var userId in recipients)
        {
            var cts = _pendingPushTracker.RegisterPending(notification.Message.Id, userId);

            _ = Task.Run(async () =>
            {
                try
                {
                    // Ждём 5 секунд перед отправкой push
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

                    // Не отменено за 5 сек = не прочитано → отправляем push
                    using var scope = _scopeFactory.CreateScope();
                    var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    // Извлекаем превью изображения из первого IMAGE вложения
                    // Используем только PreviewUrl - fileId бесполезен для FCM (Android не может загрузить по fileId)
                    string? imagePreviewUrl = null;
                    var imageAttachment = notification.Message.Content?.Attachments
                        .FirstOrDefault(a => a.Type == MessageAttachmentType.Image);
                    if (imageAttachment != null)
                    {
                        imagePreviewUrl = imageAttachment.PreviewUrl;
                        if (string.IsNullOrEmpty(imagePreviewUrl))
                        {
                            _logger.LogWarning("Image attachment has no PreviewUrl, BigPictureStyle не будет работать для сообщения {MessageId}", notification.Message.Id);
                        }
                    }

                    // Тип вложения: берём из первого attachment (MessageAttachmentType),
                    // а НЕ из Message.Type (MessageContentType: GENERIC=1, SYSTEM=2).
                    var attachmentType = imageAttachment?.Type
                        ?? notification.Message.Content?.Attachments.FirstOrDefault()?.Type
                        ?? MessageAttachmentType.Unknown;

                    await publisher.Publish(new PushNotificationEvent
                    {
                        ChatId = notification.ChatId,
                        SenderId = notification.Message.SenderId,
                        MessageId = notification.Message.Id,
                        MessageText = notification.Message.Content?.Text,
                        RecipientUserIds = [userId],
                        ContentType = (int)attachmentType,
                        ImagePreviewUrl = imagePreviewUrl,
                        AttachmentCount = notification.Message.Content?.Attachments.Count ?? 0
                    });

                    _logger.LogInformation(
                        "Push-уведомление отправлено для сообщения {MessageId} пользователю {UserId}",
                        notification.Message.Id,
                        userId);
                }
                catch (OperationCanceledException)
                {
                    // Прочитано — отменяем отправку push
                    _logger.LogDebug(
                        "Push-уведомление отменено для сообщения {MessageId} пользователю {UserId} (прочитано)",
                        notification.Message.Id,
                        userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Ошибка при отправке push-уведомления для сообщения {MessageId} пользователю {UserId}",
                        notification.Message.Id,
                        userId);
                }
                finally
                {
                    _pendingPushTracker.RemovePending(notification.Message.Id, userId);
                }
            }, CancellationToken.None);
        }
    }
}
