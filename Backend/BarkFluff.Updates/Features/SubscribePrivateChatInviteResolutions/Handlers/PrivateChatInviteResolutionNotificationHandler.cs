namespace BarkFluff.Updates.Features.SubscribePrivateChatInviteResolutions.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

public class PrivateChatInviteResolutionNotificationHandler : INotificationHandler<PrivateChatInviteResolutionNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<PrivateChatInviteResolutionNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public PrivateChatInviteResolutionNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<PrivateChatInviteResolutionNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(PrivateChatInviteResolutionNotification notification, CancellationToken cancellationToken)
    {
        var streams = _subscriptionsManager.GetUserStreams(notification.InviterUserId).ToList();
        if (streams.Count == 0)
        {
            return;
        }

        var evt = new PrivateChatInviteResolutionEvent
        {
            ChatId = notification.ChatId.ToString(),
            InviteeUserId = notification.InviteeUserId,
            Accepted = notification.Accepted
        };

        var tasks = streams.Select(stream => Task.Run(async () =>
        {
            try
            {
                await stream.WriteAsync(evt, cancellationToken);
                _metrics.Increment("private_chat_invite_resolutions_broadcast");
            }
            catch (Exception ex)
            {
                _metrics.Increment("private_chat_invite_resolutions_broadcast_errors");
                _logger.LogWarning(ex,
                    "Не удалось отправить resolution приватного чата {ChatId} инициатору {UserId}",
                    notification.ChatId, notification.InviterUserId);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }
}
