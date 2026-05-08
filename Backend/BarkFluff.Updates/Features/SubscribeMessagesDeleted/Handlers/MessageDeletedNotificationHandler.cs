namespace BarkFluff.Updates.Features.SubscribeMessagesDeleted.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeMessagesDeleted;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class MessageDeletedNotificationHandler : INotificationHandler<MessageDeletedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<MessageDeletedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public MessageDeletedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<MessageDeletedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(MessageDeletedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing message-deleted notification for chat {ChatId} with {MemberCount} members",
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
                        var deletedEvent = new MessageDeletedEvent
                        {
                            MessageId = notification.MessageId,
                            ChatId = notification.ChatId.ToString()
                        };
                        await stream.WriteAsync(deletedEvent, cancellationToken);

                        _metrics.Increment("messages_deleted_broadcast");

                        _logger.LogDebug("Successfully sent deleted message {MessageId} to user {UserId}",
                            notification.MessageId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("messages_deleted_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send deleted message {MessageId} to user {UserId} stream",
                            notification.MessageId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
