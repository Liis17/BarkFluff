namespace BarkFluff.Updates.Features.SubscribeSecretMessages.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;

using Google.Protobuf;

using Google.Protobuf.WellKnownTypes;

using MediatR;

public class NewSecretMessageNotificationHandler : INotificationHandler<NewSecretMessageNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<NewSecretMessageNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public NewSecretMessageNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<NewSecretMessageNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(NewSecretMessageNotification notification, CancellationToken cancellationToken)
    {
        var streams = _subscriptionsManager
            .GetDeviceStreams(notification.RecipientUserId, notification.RecipientDeviceId)
            .ToList();

        if (streams.Count == 0)
        {
            // Получатель оффлайн: envelope уже в SecretMessageBuffer (24ч),
            // silent push публикуется Messages-сервисом отдельно. Updates ничего не делает.
            _metrics.Increment("secret_messages_buffered_only");
            _logger.LogDebug(
                "Устройство {DeviceId} пользователя {UserId} оффлайн, секретное сообщение {MessageId} остаётся в буфере",
                notification.RecipientDeviceId, notification.RecipientUserId, notification.MessageId);
            return;
        }

        var sentAtUtc = DateTime.SpecifyKind(notification.SentAt, DateTimeKind.Utc);

        var envelope = new SecretEnvelope
        {
            MessageId = notification.MessageId,
            SenderUserId = notification.SenderUserId,
            SenderDeviceId = notification.SenderDeviceId.ToString(),
            RecipientDeviceId = notification.RecipientDeviceId.ToString(),
            Envelope = ByteString.CopyFrom(notification.Envelope),
            SentAt = Timestamp.FromDateTime(sentAtUtc)
        };

        var evt = new NewSecretMessageEvent { Envelope = envelope };

        var tasks = streams.Select(stream => Task.Run(async () =>
        {
            try
            {
                await stream.WriteAsync(evt, cancellationToken);
                _metrics.Increment("secret_messages_broadcast");
            }
            catch (Exception ex)
            {
                _metrics.Increment("secret_messages_broadcast_errors");
                _logger.LogWarning(ex,
                    "Не удалось отправить секретное сообщение {MessageId} устройству {DeviceId}",
                    notification.MessageId, notification.RecipientDeviceId);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }
}
