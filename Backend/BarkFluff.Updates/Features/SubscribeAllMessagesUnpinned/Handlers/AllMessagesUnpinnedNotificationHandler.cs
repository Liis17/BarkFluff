namespace BarkFluff.Updates.Features.SubscribeAllMessagesUnpinned.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeAllMessagesUnpinned;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class AllMessagesUnpinnedNotificationHandler : INotificationHandler<AllMessagesUnpinnedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<AllMessagesUnpinnedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public AllMessagesUnpinnedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<AllMessagesUnpinnedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(AllMessagesUnpinnedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing all-messages-unpinned notification for chat {ChatId} with {MemberCount} members",
            notification.ChatId, notification.Members.Count);

        var sendTasks = new List<Task>();

        foreach (var memberId in notification.Members)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);

            foreach (var stream in streams)
            {
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var allUnpinnedEvent = new AllMessagesUnpinnedEvent
                        {
                            ChatId = notification.ChatId.ToString()
                        };
                        await stream.WriteAsync(allUnpinnedEvent, cancellationToken);

                        _metrics.Increment("all_messages_unpinned_broadcast");

                        _logger.LogDebug("Successfully sent all-unpinned event for chat {ChatId} to user {UserId}",
                            notification.ChatId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("all_messages_unpinned_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send all-unpinned event for chat {ChatId} to user {UserId} stream",
                            notification.ChatId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
