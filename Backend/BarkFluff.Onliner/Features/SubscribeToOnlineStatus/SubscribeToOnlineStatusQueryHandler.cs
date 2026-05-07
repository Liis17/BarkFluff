using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Features.SubscribeToOnlineStatus;

public class SubscribeToOnlineStatusQueryHandler
{
    private readonly UserContext _userContext;
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly OnlineVisibilityFilter _visibilityFilter;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SubscribeToOnlineStatusQueryHandler> _logger;

    public SubscribeToOnlineStatusQueryHandler(
        UserContext userContext,
        OnlineStatusSubscriptionsManager subscriptionsManager,
        OnlineVisibilityFilter visibilityFilter,
        MetricsCollector metrics,
        ILogger<SubscribeToOnlineStatusQueryHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _visibilityFilter = visibilityFilter;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(SubscribeToOnlineStatusQuery request)
    {
        var userId = _userContext.UserId;

        _logger.LogInformation(
            "User {UserId} subscribing to online status for {Count} users",
            userId, request.UserIds.Count);

        // Отфильтровываем пользователей, скрывших онлайн-статус от подписчика.
        var visibleIds = await _visibilityFilter.GetVisibleUserIdsAsync(
            request.UserIds, userId, request.CancellationToken);

        var filteredUserIds = request.UserIds.Where(visibleIds.Contains).ToList();

        var hiddenByPrivacy = request.UserIds.Count - filteredUserIds.Count;
        if (hiddenByPrivacy > 0)
        {
            _metrics.Add("subscriptions_hidden_by_privacy", hiddenByPrivacy);
            _logger.LogDebug(
                "User {UserId} subscription filtered from {Requested} to {Visible} users by privacy",
                userId, request.UserIds.Count, filteredUserIds.Count);
        }

        // Регистрируем подписку
        var connectionId = _subscriptionsManager.RegisterSubscription(
            userId,
            filteredUserIds,
            request.ResponseStream);
        _metrics.Increment("subscriptions_registered");

        try
        {
            // Держим соединение открытым до отмены
            await Task.Delay(Timeout.Infinite, request.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение при отключении клиента
            _logger.LogInformation("User {UserId} subscription cancelled", userId);
        }
        finally
        {
            // Cleanup при отключении
            _subscriptionsManager.RemoveSubscription(userId, connectionId);
            _metrics.Increment("subscriptions_disconnected");
            _logger.LogDebug("User {UserId} subscription {ConnectionId} removed", userId, connectionId);
        }
    }
}
