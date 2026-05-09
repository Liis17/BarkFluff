namespace BarkFluff.Updates.Features.SubscribeSecretChatInvites.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using Google.Protobuf;

using Google.Protobuf.WellKnownTypes;

using MediatR;

public class SecretChatInviteNotificationHandler : INotificationHandler<SecretChatInviteNotification>
{
    // По умолчанию TTL инвайта в Redis-буфере = 24 часа.
    // Сервер указывает клиенту, когда инвайт удалится.
    private static readonly TimeSpan InviteTtl = TimeSpan.FromHours(24);

    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<SecretChatInviteNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public SecretChatInviteNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<SecretChatInviteNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(SecretChatInviteNotification notification, CancellationToken cancellationToken)
    {
        var streams = _subscriptionsManager
            .GetDeviceStreams(notification.RecipientUserId, notification.RecipientDeviceId)
            .ToList();

        if (streams.Count == 0)
        {
            // Устройство-получатель оффлайн: invite уже в Redis-буфере (24ч),
            // silent push отправлен Messages-сервисом — здесь ничего не делаем.
            _metrics.Increment("secret_chat_invites_buffered_only");
            _logger.LogDebug(
                "Устройство {DeviceId} пользователя {UserId} оффлайн, invite {InviteId} остаётся в буфере",
                notification.RecipientDeviceId, notification.RecipientUserId, notification.InviteId);
            return;
        }

        var sentAtUtc = DateTime.SpecifyKind(notification.SentAt, DateTimeKind.Utc);
        var expiresAtUtc = sentAtUtc.Add(InviteTtl);

        var evt = new SecretChatInviteEvent
        {
            InviteId = notification.InviteId,
            SenderUserId = notification.SenderUserId,
            SenderDeviceId = notification.SenderDeviceId.ToString(),
            InitialEnvelope = ByteString.CopyFrom(notification.InitialEnvelope),
            InvitedAt = Timestamp.FromDateTime(sentAtUtc),
            ExpiresAt = Timestamp.FromDateTime(expiresAtUtc)
        };

        var tasks = streams.Select(stream => Task.Run(async () =>
        {
            try
            {
                await stream.WriteAsync(evt, cancellationToken);
                _metrics.Increment("secret_chat_invites_broadcast");
            }
            catch (Exception ex)
            {
                _metrics.Increment("secret_chat_invites_broadcast_errors");
                _logger.LogWarning(ex,
                    "Не удалось отправить invite секретного чата {InviteId} устройству {DeviceId}",
                    notification.InviteId, notification.RecipientDeviceId);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }
}
