namespace BarkFluff.Updates.Features.SubscribeMessagesEdited.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeMessagesEdited;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class MessageEditedNotificationHandler : INotificationHandler<MessageEditedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<MessageEditedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public MessageEditedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<MessageEditedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(MessageEditedNotification notification, CancellationToken cancellationToken)
    {
        var message = notification.Message;

        _logger.LogDebug("Processing message-edited notification for chat {ChatId} with {MemberCount} members",
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
                        var editedEvent = new MessageEditedEvent
                        {
                            Message = message,
                            ChatId = notification.ChatId.ToString()
                        };
                        await stream.WriteAsync(editedEvent, cancellationToken);

                        _metrics.Increment("messages_edited_broadcast");

                        _logger.LogDebug("Successfully sent edited message {MessageId} to user {UserId}",
                            message.Id, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("messages_edited_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send edited message {MessageId} to user {UserId} stream",
                            message.Id, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
