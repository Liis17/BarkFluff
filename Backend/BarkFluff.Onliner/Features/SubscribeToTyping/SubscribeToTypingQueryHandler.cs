using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Features.SubscribeToTyping;

public class SubscribeToTypingQueryHandler
{
    private readonly UserContext _userContext;
    private readonly TypingSubscriptionsManager _subscriptionsManager;
    private readonly ChatMembershipFilter _membershipFilter;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SubscribeToTypingQueryHandler> _logger;

    public SubscribeToTypingQueryHandler(
        UserContext userContext,
        TypingSubscriptionsManager subscriptionsManager,
        ChatMembershipFilter membershipFilter,
        MetricsCollector metrics,
        ILogger<SubscribeToTypingQueryHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _membershipFilter = membershipFilter;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(SubscribeToTypingQuery request)
    {
        var userId = _userContext.UserId;

        _logger.LogInformation(
            "User {UserId} subscribing to typing for {Count} chats",
            userId, request.ChatIds.Count);

        // Оставляем только чаты, в которых пользователь действительно состоит (fail-closed).
        var membership = await _membershipFilter.GetMemberChatIdsAsync(
            userId, request.ChatIds, request.CancellationToken);

        var filteredChatIds = request.ChatIds.Where(membership.MemberChatIds.Contains).ToList();

        var hiddenByMembership = request.ChatIds.Distinct().Count() - filteredChatIds.Distinct().Count();
        if (hiddenByMembership > 0)
        {
            _metrics.Add("typing_subscriptions_hidden_by_membership", hiddenByMembership);
            _logger.LogDebug(
                "User {UserId} typing subscription filtered from {Requested} to {Member} chats by membership",
                userId, request.ChatIds.Count, filteredChatIds.Count);
        }

        var connectionId = _subscriptionsManager.RegisterSubscription(
            userId,
            filteredChatIds,
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
