using BarkFluff.Proto.Onliner;

using Grpc.Core;

using System.Collections.Concurrent;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Управляет подписками на изменения онлайн-статусов.
/// Singleton сервис.
/// Поддерживает обратный индекс trackedUserId -> подписки для O(1) выборки в GetStreamsTrackingUser.
/// С этапа 4.2 рядом живёт параллельный обратный индекс по UUID (remote-пользователи, у которых
/// нет локального long-идентификатора) — структура и потокобезопасность те же.
/// </summary>
public class OnlineStatusSubscriptionsManager
{
    // Прямой индекс: SubscriberId (UserId подписчика) -> (ConnectionId -> SubscriptionData)
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, SubscriptionData>>
        _subscriptions = new();

    // Обратный индекс: TrackedUserId -> (ConnectionId -> Stream).
    // Позволяет получать список stream'ов, отслеживающих конкретного пользователя, без перебора всех подписок.
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, IServerStreamWriter<UserOnlineStatus>>>
        _reverseIndex = new();

    // Обратный индекс по UUID: TrackedUuid -> (ConnectionId -> Stream). Пара к _reverseIndex.
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, IServerStreamWriter<UserOnlineStatus>>>
        _reverseUuidIndex = new();

    /// <summary>
    /// Данные подписки.
    /// </summary>
    public class SubscriptionData
    {
        public required IServerStreamWriter<UserOnlineStatus> Stream { get; init; }
        public required HashSet<long> TrackedUserIds { get; init; }
        public required HashSet<Guid> TrackedUuids { get; init; }
    }

    /// <summary>
    /// Регистрация новой подписки.
    /// </summary>
    public Guid RegisterSubscription(
        long subscriberId,
        List<long> trackedUserIds,
        IServerStreamWriter<UserOnlineStatus> responseStream,
        List<Guid>? trackedUuids = null)
    {
        var connectionId = Guid.NewGuid();
        var trackedSet = new HashSet<long>(trackedUserIds);
        var trackedUuidSet = trackedUuids is null ? [] : new HashSet<Guid>(trackedUuids);

        var userSubscriptions = _subscriptions.GetOrAdd(
            subscriberId,
            _ => new ConcurrentDictionary<Guid, SubscriptionData>()
        );

        userSubscriptions[connectionId] = new SubscriptionData
        {
            Stream = responseStream,
            TrackedUserIds = trackedSet,
            TrackedUuids = trackedUuidSet
        };

        AddToReverseIndex(connectionId, trackedSet, responseStream);
        AddToReverseUuidIndex(connectionId, trackedUuidSet, responseStream);

        return connectionId;
    }

    /// <summary>
    /// Удаление подписки при отключении клиента.
    /// </summary>
    public void RemoveSubscription(long subscriberId, Guid connectionId)
    {
        if (!_subscriptions.TryGetValue(subscriberId, out var userSubscriptions))
        {
            return;
        }

        if (userSubscriptions.TryRemove(connectionId, out var removed))
        {
            RemoveFromReverseIndex(connectionId, removed.TrackedUserIds);
            RemoveFromReverseUuidIndex(connectionId, removed.TrackedUuids);
        }

        // Очистка пустых записей
        if (userSubscriptions.IsEmpty)
        {
            _subscriptions.TryRemove(subscriberId, out _);
        }
    }

    /// <summary>
    /// Получить все streams которые отслеживают данного пользователя.
    /// O(1) за счёт обратного индекса.
    /// </summary>
    public List<IServerStreamWriter<UserOnlineStatus>> GetStreamsTrackingUser(long userId)
    {
        if (!_reverseIndex.TryGetValue(userId, out var connectionStreams))
        {
            return [];
        }

        return connectionStreams.Values.ToList();
    }

    /// <summary>
    /// Получить все streams, которые отслеживают remote-пользователя по UUID.
    /// Симметрично <see cref="GetStreamsTrackingUser"/>.
    /// </summary>
    public List<IServerStreamWriter<UserOnlineStatus>> GetStreamsTrackingUuid(Guid uuid)
    {
        if (!_reverseUuidIndex.TryGetValue(uuid, out var connectionStreams))
        {
            return [];
        }

        return connectionStreams.Values.ToList();
    }

    /// <summary>
    /// Снимок всех отслеживаемых этим инстансом UUID — интерес, о котором
    /// <see cref="BackgroundServices.PresenceInterestReporter"/> сообщает Federation.
    /// </summary>
    public List<Guid> GetTrackedUuids() => _reverseUuidIndex.Keys.ToList();

    /// <summary>
    /// Обновить TrackedUserIds во всех активных подписках пользователя.
    /// </summary>
    /// <param name="subscriberId">ID пользователя-подписчика</param>
    /// <param name="newUserIds">Новый список отслеживаемых пользователей</param>
    /// <param name="newUuids">Новый список отслеживаемых remote-пользователей (UUID)</param>
    /// <returns>Количество обновленных подписок (0 если нет активных)</returns>
    public int UpdateAllSubscriptions(long subscriberId, List<long> newUserIds, List<Guid>? newUuids = null)
    {
        if (!_subscriptions.TryGetValue(subscriberId, out var userSubscriptions))
        {
            return 0;
        }

        foreach (var (connectionId, oldSubscription) in userSubscriptions.ToList())
        {
            // Создаём свой набор для каждой подписки, чтобы внутренние HashSet'ы не шарились
            var newSet = new HashSet<long>(newUserIds);
            var newUuidSet = newUuids is null ? [] : new HashSet<Guid>(newUuids);

            var newSubscription = new SubscriptionData
            {
                Stream = oldSubscription.Stream,
                TrackedUserIds = newSet,
                TrackedUuids = newUuidSet
            };

            // Атомарная замена подписки
            if (userSubscriptions.TryUpdate(connectionId, newSubscription, oldSubscription))
            {
                // Синхронизируем обратные индексы с новыми наборами отслеживаемых пользователей
                RemoveFromReverseIndex(connectionId, oldSubscription.TrackedUserIds);
                AddToReverseIndex(connectionId, newSet, oldSubscription.Stream);

                RemoveFromReverseUuidIndex(connectionId, oldSubscription.TrackedUuids);
                AddToReverseUuidIndex(connectionId, newUuidSet, oldSubscription.Stream);
            }
        }

        return userSubscriptions.Count;
    }

    /// <summary>
    /// Текущее количество активных подписок (gRPC streams) — для метрик.
    /// </summary>
    public int GetActiveSubscriptionsCount()
    {
        var count = 0;
        foreach (var kvp in _subscriptions)
        {
            count += kvp.Value.Count;
        }
        return count;
    }

    /// <summary>
    /// Количество уникальных отслеживаемых пользователей (размер обратного индекса) — для метрик.
    /// </summary>
    public int GetTrackedUniqueUsersCount() => _reverseIndex.Count;

    /// <summary>
    /// Количество уникальных отслеживаемых remote-пользователей — для метрик.
    /// </summary>
    public int GetTrackedUniqueUuidsCount() => _reverseUuidIndex.Count;

    private void AddToReverseIndex(
        Guid connectionId,
        IEnumerable<long> trackedUserIds,
        IServerStreamWriter<UserOnlineStatus> stream)
    {
        foreach (var trackedId in trackedUserIds)
        {
            var connections = _reverseIndex.GetOrAdd(
                trackedId,
                _ => new ConcurrentDictionary<Guid, IServerStreamWriter<UserOnlineStatus>>());

            connections[connectionId] = stream;
        }
    }

    private void RemoveFromReverseIndex(Guid connectionId, IEnumerable<long> trackedUserIds)
    {
        foreach (var trackedId in trackedUserIds)
        {
            if (_reverseIndex.TryGetValue(trackedId, out var connections))
            {
                connections.TryRemove(connectionId, out _);

                if (connections.IsEmpty)
                {
                    _reverseIndex.TryRemove(trackedId, out _);
                }
            }
        }
    }

    private void AddToReverseUuidIndex(
        Guid connectionId,
        IEnumerable<Guid> trackedUuids,
        IServerStreamWriter<UserOnlineStatus> stream)
    {
        foreach (var uuid in trackedUuids)
        {
            var connections = _reverseUuidIndex.GetOrAdd(
                uuid,
                _ => new ConcurrentDictionary<Guid, IServerStreamWriter<UserOnlineStatus>>());

            connections[connectionId] = stream;
        }
    }

    private void RemoveFromReverseUuidIndex(Guid connectionId, IEnumerable<Guid> trackedUuids)
    {
        foreach (var uuid in trackedUuids)
        {
            if (_reverseUuidIndex.TryGetValue(uuid, out var connections))
            {
                connections.TryRemove(connectionId, out _);

                if (connections.IsEmpty)
                {
                    _reverseUuidIndex.TryRemove(uuid, out _);
                }
            }
        }
    }
}
