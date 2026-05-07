using BarkFluff.Proto.Onliner;

using Grpc.Core;

using System.Collections.Concurrent;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Управляет подписками на изменения онлайн-статусов.
/// Singleton сервис.
/// Поддерживает обратный индекс trackedUserId -> подписки для O(1) выборки в GetStreamsTrackingUser.
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

    /// <summary>
    /// Данные подписки.
    /// </summary>
    public class SubscriptionData
    {
        public required IServerStreamWriter<UserOnlineStatus> Stream { get; init; }
        public required HashSet<long> TrackedUserIds { get; init; }
    }

    /// <summary>
    /// Регистрация новой подписки.
    /// </summary>
    public Guid RegisterSubscription(
        long subscriberId,
        List<long> trackedUserIds,
        IServerStreamWriter<UserOnlineStatus> responseStream)
    {
        var connectionId = Guid.NewGuid();
        var trackedSet = new HashSet<long>(trackedUserIds);

        var userSubscriptions = _subscriptions.GetOrAdd(
            subscriberId,
            _ => new ConcurrentDictionary<Guid, SubscriptionData>()
        );

        userSubscriptions[connectionId] = new SubscriptionData
        {
            Stream = responseStream,
            TrackedUserIds = trackedSet
        };

        AddToReverseIndex(connectionId, trackedSet, responseStream);

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
    /// Обновить TrackedUserIds во всех активных подписках пользователя.
    /// </summary>
    /// <param name="subscriberId">ID пользователя-подписчика</param>
    /// <param name="newUserIds">Новый список отслеживаемых пользователей</param>
    /// <returns>Количество обновленных подписок (0 если нет активных)</returns>
    public int UpdateAllSubscriptions(long subscriberId, List<long> newUserIds)
    {
        if (!_subscriptions.TryGetValue(subscriberId, out var userSubscriptions))
        {
            return 0;
        }

        foreach (var (connectionId, oldSubscription) in userSubscriptions.ToList())
        {
            // Создаём свой набор для каждой подписки, чтобы внутренние HashSet'ы не шарились
            var newSet = new HashSet<long>(newUserIds);

            var newSubscription = new SubscriptionData
            {
                Stream = oldSubscription.Stream,
                TrackedUserIds = newSet
            };

            // Атомарная замена подписки
            if (userSubscriptions.TryUpdate(connectionId, newSubscription, oldSubscription))
            {
                // Синхронизируем обратный индекс с новым набором отслеживаемых пользователей
                RemoveFromReverseIndex(connectionId, oldSubscription.TrackedUserIds);
                AddToReverseIndex(connectionId, newSet, oldSubscription.Stream);
            }
        }

        return userSubscriptions.Count;
    }

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
}
