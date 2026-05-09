namespace BarkFluff.Updates.Features.SubscribeMessagesPinned.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeMessagesPinned;

using Google.Protobuf.WellKnownTypes;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class MessagePinnedNotificationHandler : INotificationHandler<MessagePinnedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<MessagePinnedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public MessagePinnedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<MessagePinnedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(MessagePinnedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing message-pinned notification for chat {ChatId} with {MemberCount} members",
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
                        var pinnedEvent = new MessagePinnedEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            MessageId = notification.MessageId,
                            PinnerUserId = notification.PinnerUserId,
                            PinnedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(notification.PinnedAt, DateTimeKind.Utc))
                        };
                        await stream.WriteAsync(pinnedEvent, cancellationToken);

                        _metrics.Increment("messages_pinned_broadcast");

                        _logger.LogDebug("Successfully sent pinned message {MessageId} to user {UserId}",
                            notification.MessageId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("messages_pinned_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send pinned message {MessageId} to user {UserId} stream",
                            notification.MessageId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
