using System.Collections.Concurrent;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Реестр активных входящих presence-подписок (этап 4.3): кто следит, за сколькими нашими
/// пользователями и с какого времени. Нужен доставке изменений, метрикам и статусу федерации.
/// </summary>
public class IncomingPresenceRegistry
{
    private readonly ConcurrentDictionary<Guid, IncomingPresenceSubscription> _subscriptions = new();

    public IncomingPresenceSubscription Add(string origin, IReadOnlyDictionary<long, Guid> watched)
    {
        var subscription = new IncomingPresenceSubscription(origin, watched);
        _subscriptions[subscription.Id] = subscription;
        return subscription;
    }

    public void Remove(Guid subscriptionId) => _subscriptions.TryRemove(subscriptionId, out _);

    /// <summary>
    /// Статус локального пользователя изменился — пометить его во всех подписках, которые за ним
    /// следят. Реальная отправка (с coalescing) происходит в цикле стрима.
    /// </summary>
    public void MarkStatusChanged(long userId)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.MarkDirty(userId);
        }
    }

    public int Count => _subscriptions.Count;

    /// <summary>Суммарное число наблюдаемых пар (подписка, пользователь) — для метрик.</summary>
    public int WatchedTotal => _subscriptions.Values.Sum(s => s.Watched.Count);

    public IReadOnlyCollection<IncomingPresenceSubscription> Snapshot() => _subscriptions.Values.ToList();
}
