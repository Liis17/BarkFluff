namespace BarkFluff.Updates.Features.SubscribePrivateChatInvites.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using Google.Protobuf;

using Google.Protobuf.WellKnownTypes;

using MediatR;

public class PrivateChatInviteNotificationHandler : INotificationHandler<PrivateChatInviteNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<PrivateChatInviteNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public PrivateChatInviteNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<PrivateChatInviteNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(PrivateChatInviteNotification notification, CancellationToken cancellationToken)
    {
        var streams = _subscriptionsManager.GetUserStreams(notification.InviteeUserId).ToList();
        if (streams.Count == 0)
        {
            _logger.LogDebug(
                "Нет активных подписок на инвайты приватных чатов для пользователя {UserId}; чат {ChatId}",
                notification.InviteeUserId, notification.ChatId);
            return;
        }

        var evt = new PrivateChatInviteEvent
        {
            ChatId = notification.ChatId.ToString(),
            InviterUserId = notification.InviterUserId,
            KdfSalt = ByteString.CopyFrom(notification.KdfSalt),
            PassphraseVerifier = ByteString.CopyFrom(notification.PassphraseVerifier),
            InvitedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(notification.InvitedAt, DateTimeKind.Utc))
        };

        var tasks = streams.Select(stream => Task.Run(async () =>
        {
            try
            {
                await stream.WriteAsync(evt, cancellationToken);
                _metrics.Increment("private_chat_invites_broadcast");
            }
            catch (Exception ex)
            {
                _metrics.Increment("private_chat_invites_broadcast_errors");
                _logger.LogWarning(ex,
                    "Не удалось отправить invite приватного чата {ChatId} пользователю {UserId}",
                    notification.ChatId, notification.InviteeUserId);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }
}
