using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Features.SubscribeToTyping;

public class SubscribeToTypingQueryHandler
{
    private readonly UserContext _userContext;
    private readonly TypingSubscriptionsManager _subscriptionsManager;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SubscribeToTypingQueryHandler> _logger;

    public SubscribeToTypingQueryHandler(
        UserContext userContext,
        TypingSubscriptionsManager subscriptionsManager,
        MetricsCollector metrics,
        ILogger<SubscribeToTypingQueryHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(SubscribeToTypingQuery request)
    {
        var userId = _userContext.UserId;

        _logger.LogInformation(
            "User {UserId} subscribing to typing for {Count} chats",
            userId, request.ChatIds.Count);

        var connectionId = _subscriptionsManager.RegisterSubscription(
            userId,
            request.ChatIds,
            request.ResponseStream);
        _metrics.Increment("typing_subscriptions_registered");

        try
        {
            await Task.Delay(Timeout.Infinite, request.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("User {UserId} typing subscription cancelled", userId);
        }
        finally
        {
            _subscriptionsManager.RemoveSubscription(userId, connectionId);
            _metrics.Increment("typing_subscriptions_disconnected");
            _logger.LogDebug("User {UserId} typing subscription {ConnectionId} removed", userId, connectionId);
        }
    }
}
