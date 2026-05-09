namespace BarkFluff.Updates.Features.SubscribeMessagesUnpinned.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeMessagesUnpinned;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class MessageUnpinnedNotificationHandler : INotificationHandler<MessageUnpinnedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<MessageUnpinnedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public MessageUnpinnedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<MessageUnpinnedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(MessageUnpinnedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing message-unpinned notification for chat {ChatId} with {MemberCount} members",
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
                        var unpinnedEvent = new MessageUnpinnedEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            MessageId = notification.MessageId
                        };
                        await stream.WriteAsync(unpinnedEvent, cancellationToken);

                        _metrics.Increment("messages_unpinned_broadcast");

                        _logger.LogDebug("Successfully sent unpinned message {MessageId} to user {UserId}",
                            notification.MessageId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("messages_unpinned_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send unpinned message {MessageId} to user {UserId} stream",
                            notification.MessageId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
