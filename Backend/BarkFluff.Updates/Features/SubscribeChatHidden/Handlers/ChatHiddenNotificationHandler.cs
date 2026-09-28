namespace BarkFluff.Updates.Features.SubscribeChatHidden.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeChatHidden;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class ChatHiddenNotificationHandler : INotificationHandler<ChatHiddenNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<ChatHiddenNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public ChatHiddenNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<ChatHiddenNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(ChatHiddenNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing chat-hidden notification for chat {ChatId} with {MemberCount} members",
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
                        var hiddenEvent = new ChatHiddenEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            ContentWiped = notification.ContentWiped
                        };
                        await stream.WriteAsync(hiddenEvent, cancellationToken);

                        _metrics.Increment("chats_hidden_broadcast");

                        _logger.LogDebug("Successfully sent chat-hidden {ChatId} to user {UserId}",
                            notification.ChatId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("chats_hidden_broadcast_errors");
                        _logger.LogWarning(ex, "Failed to send chat-hidden {ChatId} to user {UserId} stream",
                            notification.ChatId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
