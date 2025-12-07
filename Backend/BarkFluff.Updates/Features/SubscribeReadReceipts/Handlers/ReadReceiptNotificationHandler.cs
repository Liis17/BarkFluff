namespace BarkFluff.Updates.Features.SubscribeReadReceipts.Handlers;

using System.Threading;
using System.Threading.Tasks;
using BarkFluff.Proto.Updates;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.Logging;

public class ReadReceiptNotificationHandler : INotificationHandler<ReadReceiptNotification>
{
    private readonly ReadReceiptSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<ReadReceiptNotificationHandler> _logger;

    public ReadReceiptNotificationHandler(
        ReadReceiptSubscriptionsManager subscriptionsManager,
        ILogger<ReadReceiptNotificationHandler> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
    }

    public async Task Handle(ReadReceiptNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Processing read receipt notification for message {MessageId} in chat {ChatId}, IsLastMessage: {IsLastMessage}",
            notification.MessageId, notification.ChatId, notification.IsLastMessage);

        var update = new ReadReceiptUpdate
        {
            ChatId = notification.ChatId,
            MessageId = notification.MessageId,
            ReadAt = Timestamp.FromDateTime(notification.ReadAt.ToUniversalTime())
        };
        update.ReadBy.AddRange(notification.ReadBy);

        // Send to all chat members except those who already read it
        foreach (var memberId in notification.ChatMembers)
        {
            // Skip sending to users who already read the message
            if (notification.ReadBy.Contains(memberId))
            {
                continue;
            }

            // Send to chat-specific subscriptions (open chat)
            var chatStreams = _subscriptionsManager.GetChatStreams(memberId, notification.ChatId);
            foreach (var stream in chatStreams)
            {
                try
                {
                    await stream.WriteAsync(update, cancellationToken);
                    _logger.LogDebug(
                        "Sent read receipt for message {MessageId} to user {UserId} (chat subscription)",
                        notification.MessageId, memberId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send read receipt for message {MessageId} to user {UserId} chat stream",
                        notification.MessageId, memberId);
                }
            }

            // Send to global subscriptions (chat list) only if it's the last message
            if (notification.IsLastMessage)
            {
                var globalStreams = _subscriptionsManager.GetGlobalStreams(memberId);
                foreach (var stream in globalStreams)
                {
                    try
                    {
                        await stream.WriteAsync(update, cancellationToken);
                        _logger.LogDebug(
                            "Sent read receipt for last message {MessageId} to user {UserId} (global subscription)",
                            notification.MessageId, memberId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to send read receipt for message {MessageId} to user {UserId} global stream",
                            notification.MessageId, memberId);
                    }
                }
            }
        }
    }
}
