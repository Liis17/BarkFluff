namespace BarkFluff.Updates.Features.SubscribeSecretChatResolutions.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using Google.Protobuf;

using MediatR;

public class SecretChatInviteResolutionNotificationHandler : INotificationHandler<SecretChatInviteResolutionNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<SecretChatInviteResolutionNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public SecretChatInviteResolutionNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<SecretChatInviteResolutionNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(SecretChatInviteResolutionNotification notification, CancellationToken cancellationToken)
    {
        // Resolution маршрутизируется на устройство-инициатора инвайта (sender_device_id).
        var streams = _subscriptionsManager
            .GetDeviceStreams(notification.SenderUserId, notification.SenderDeviceId)
            .ToList();

        if (streams.Count == 0)
        {
            _metrics.Increment("secret_chat_resolutions_no_active_stream");
            _logger.LogDebug(
                "Устройство-инициатор {DeviceId} пользователя {UserId} оффлайн, resolution {InviteId} не доставлено в стрим",
                notification.SenderDeviceId, notification.SenderUserId, notification.InviteId);
            return;
        }

        var evt = new SecretChatInviteResolutionEvent
        {
            InviteId = notification.InviteId,
            RecipientUserId = notification.RecipientUserId,
            RecipientDeviceId = notification.RecipientDeviceId.ToString(),
            Accepted = notification.Accepted,
            ResponseEnvelope = ByteString.CopyFrom(notification.ResponseEnvelope)
        };

        var tasks = streams.Select(stream => Task.Run(async () =>
        {
            try
            {
                await stream.WriteAsync(evt, cancellationToken);
                _metrics.Increment("secret_chat_resolutions_broadcast");
            }
            catch (Exception ex)
            {
                _metrics.Increment("secret_chat_resolutions_broadcast_errors");
                _logger.LogWarning(ex,
                    "Не удалось отправить resolution секретного чата {InviteId} устройству {DeviceId}",
                    notification.InviteId, notification.SenderDeviceId);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }
}
