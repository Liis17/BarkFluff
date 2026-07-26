using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Onliner.Features.SubscribeToOnlineStatus;

public class SubscribeToOnlineStatusQueryHandler
{
    private readonly UserContext _userContext;
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly OnlineVisibilityFilter _visibilityFilter;
    private readonly IRemotePresenceStore _remotePresence;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SubscribeToOnlineStatusQueryHandler> _logger;

    public SubscribeToOnlineStatusQueryHandler(
        UserContext userContext,
        OnlineStatusSubscriptionsManager subscriptionsManager,
        OnlineVisibilityFilter visibilityFilter,
        IRemotePresenceStore remotePresence,
        MetricsCollector metrics,
        ILogger<SubscribeToOnlineStatusQueryHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _visibilityFilter = visibilityFilter;
        _remotePresence = remotePresence;
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

        // Регистрируем подписку. Remote-uuid privacy не фильтруются: их владелец — чужая нода,
        // она уже применила свои настройки (этап 4.2).
        var connectionId = _subscriptionsManager.RegisterSubscription(
            userId,
            filteredUserIds,
            request.ResponseStream,
            request.UserUuids);
        _metrics.Increment("subscriptions_registered");

        // Начальный снимок remote-статусов: без него подписчик не узнает текущее состояние
        // до первого изменения на чужой ноде (для локальных снимок берётся GetOnlineStatus).
        await SendRemoteSnapshotAsync(request);

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

    /// <summary>
    /// Отдать в стрим текущее содержимое кеша remote-статусов. Неизвестный uuid — это
    /// <c>UNKNOWN</c>, а не <c>OFFLINE</c>: «нода-партнёр статус не отдаёт» и «человек не в сети» —
    /// разные вещи, и путать их нельзя.
    /// </summary>
    private async Task SendRemoteSnapshotAsync(SubscribeToOnlineStatusQuery request)
    {
        if (request.UserUuids.Count == 0)
        {
            return;
        }

        var known = await _remotePresence.GetManyAsync(request.UserUuids, request.CancellationToken);

        foreach (var uuid in request.UserUuids)
        {
            var status = known.TryGetValue(uuid, out var entry)
                ? new UserOnlineStatus
                {
                    UserUuid = uuid.ToString(),
                    Status = MapStatus(entry.Status),
                    LastSeen = Timestamp.FromDateTime(entry.LastSeen.ToUniversalTime()),
                }
                : new UserOnlineStatus
                {
                    UserUuid = uuid.ToString(),
                    Status = StatusTypeId.Unknown,
                    LastSeen = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime()),
                };

            try
            {
                await request.ResponseStream.WriteAsync(status, request.CancellationToken);
            }
            catch (Exception ex)
            {
                // Стрим мог закрыться сразу после подписки — не повод валить обработчик.
                _metrics.Increment("remote_snapshot_errors");
                _logger.LogDebug(ex, "Failed to send remote presence snapshot for {UserUuid}", uuid);
                return;
            }
        }
    }

    private static StatusTypeId MapStatus(Domain.Enums.StatusTypeId domainStatus) => domainStatus switch
    {
        Domain.Enums.StatusTypeId.Online => StatusTypeId.StatusOnline,
        Domain.Enums.StatusTypeId.Offline => StatusTypeId.StatusOffline,
        _ => StatusTypeId.Unknown
    };
}
