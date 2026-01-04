using System.Collections.Concurrent;
using BarkFluff.Proto.Onliner;
using Grpc.Core;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Управляет подписками на изменения онлайн-статусов
/// Singleton сервис
/// </summary>
public class OnlineStatusSubscriptionsManager
{
    // SubscriberId (UserId подписчика) -> (ConnectionId -> SubscriptionData)
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, SubscriptionData>>
        _subscriptions = new();

    /// <summary>
    /// Данные подписки
    /// </summary>
    public class SubscriptionData
    {
        public required IServerStreamWriter<UserOnlineStatus> Stream { get; init; }
        public required HashSet<long> TrackedUserIds { get; init; } // Список userId за которыми следим
    }

    /// <summary>
    /// Регистрация новой подписки
    /// </summary>
    public Guid RegisterSubscription(
        long subscriberId,
        List<long> trackedUserIds,
        IServerStreamWriter<UserOnlineStatus> responseStream)
    {
        var connectionId = Guid.NewGuid();

        var userSubscriptions = _subscriptions.GetOrAdd(
            subscriberId,
            _ => new ConcurrentDictionary<Guid, SubscriptionData>()
        );

        userSubscriptions[connectionId] = new SubscriptionData
        {
            Stream = responseStream,
            TrackedUserIds = new HashSet<long>(trackedUserIds)
        };

        return connectionId;
    }

    /// <summary>
    /// Удаление подписки при отключении клиента
    /// </summary>
    public void RemoveSubscription(long subscriberId, Guid connectionId)
    {
        if (_subscriptions.TryGetValue(subscriberId, out var userSubscriptions))
        {
            userSubscriptions.TryRemove(connectionId, out _);

            // Очистка пустых записей
            if (userSubscriptions.IsEmpty)
            {
                _subscriptions.TryRemove(subscriberId, out _);
            }
        }
    }

    /// <summary>
    /// Получить все streams которые отслеживают данного пользователя
    /// </summary>
    public List<IServerStreamWriter<UserOnlineStatus>> GetStreamsTrackingUser(long userId)
    {
        var streams = new List<IServerStreamWriter<UserOnlineStatus>>();

        foreach (var subscriberKvp in _subscriptions)
        {
            foreach (var subscriptionKvp in subscriberKvp.Value)
            {
                var subscription = subscriptionKvp.Value;

                // Проверяем интересуется ли эта подписка данным userId
                if (subscription.TrackedUserIds.Contains(userId))
                {
                    streams.Add(subscription.Stream);
                }
            }
        }

        return streams;
    }

    /// <summary>
    /// Обновить TrackedUserIds во всех активных подписках пользователя
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

        // Обновляем каждую подписку пользователя
        foreach (var (connectionId, oldSubscription) in userSubscriptions.ToList())
        {
            // Создаем новый SubscriptionData с обновленным списком
            var newSubscription = new SubscriptionData
            {
                Stream = oldSubscription.Stream,
                TrackedUserIds = new HashSet<long>(newUserIds)
            };

            // Атомарная замена подписки
            userSubscriptions.TryUpdate(connectionId, newSubscription, oldSubscription);
        }

        return userSubscriptions.Count;
    }
}
