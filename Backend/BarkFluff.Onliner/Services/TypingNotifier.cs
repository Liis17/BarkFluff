using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Onliner;

using Grpc.Core;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Ретранслирует события набора текста подписчикам чата.
/// Singleton, инъектируется в handler команды SetTypingStatus.
/// </summary>
public class TypingNotifier
{
    private readonly TypingSubscriptionsManager _subscriptionsManager;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<TypingNotifier> _logger;

    public TypingNotifier(
        TypingSubscriptionsManager subscriptionsManager,
        MetricsCollector metrics,
        ILogger<TypingNotifier> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Разослать индикатор набора всем подписчикам чата, кроме самого печатающего.
    /// </summary>
    public async Task NotifyTyping(
        long chatId,
        long typingUserId,
        TypingAction action,
        CancellationToken cancellationToken = default)
    {
        var streams = _subscriptionsManager.GetStreamsTrackingChat(chatId, typingUserId);

        if (streams.Count == 0)
        {
            _logger.LogTrace("No subscribers for typing in chat {ChatId}", chatId);
            return;
        }

        var typingEvent = new TypingEvent
        {
            ChatId = chatId,
            UserId = typingUserId,
            Action = action
        };

        _logger.LogTrace(
            "Relaying typing ({Action}) from user {UserId} in chat {ChatId} to {StreamCount} subscribers",
            action, typingUserId, chatId, streams.Count);

        var tasks = streams.Select(stream => SendToStreamAsync(stream, typingEvent, chatId, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task SendToStreamAsync(
        IServerStreamWriter<TypingEvent> stream,
        TypingEvent typingEvent,
        long chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            await stream.WriteAsync(typingEvent, cancellationToken);
            _metrics.Increment("typing_notifications_sent");
        }
        catch (Exception ex)
        {
            // Stream может быть закрыт — это нормально, cleanup произойдёт в gRPC service
            _metrics.Increment("typing_notification_errors");
            _logger.LogWarning(ex,
                "Failed to relay typing for chat {ChatId} - stream likely disconnected",
                chatId);
        }
    }
}
