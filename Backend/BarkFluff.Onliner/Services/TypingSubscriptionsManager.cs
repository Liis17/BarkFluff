using BarkFluff.Proto.Onliner;

using Grpc.Core;

using System.Collections.Concurrent;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Управляет подписками на индикаторы набора текста.
/// Singleton сервис. Ничего не хранит про сами события — чистый ретранслятор.
/// Поддерживает обратный индекс chatId -> подписки для O(1) выборки в GetStreamsTrackingChat.
/// </summary>
public class TypingSubscriptionsManager
{
    // Прямой индекс: SubscriberId (UserId подписчика) -> (ConnectionId -> SubscriptionData)
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, SubscriptionData>>
        _subscriptions = new();

    // Обратный индекс: ChatId (Guid-строка) -> (ConnectionId -> ReverseEntry).
    // Позволяет получать стримы, отслеживающие конкретный чат, без перебора всех подписок.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ReverseEntry>>
        _reverseIndex = new();

    /// <summary>
    /// Данные подписки.
    /// </summary>
    public class SubscriptionData
    {
        public required IServerStreamWriter<TypingEvent> Stream { get; init; }
        public required HashSet<string> TrackedChatIds { get; init; }
    }

    /// <summary>
    /// Запись обратного индекса. Хранит владельца, чтобы не ретранслировать набор обратно отправителю.
    /// </summary>
    public record ReverseEntry(IServerStreamWriter<TypingEvent> Stream, long SubscriberId);

    /// <summary>
    /// Регистрация новой подписки.
    /// </summary>
    public Guid RegisterSubscription(
        long subscriberId,
        List<string> trackedChatIds,
        IServerStreamWriter<TypingEvent> responseStream)
    {
        var connectionId = Guid.NewGuid();
        var trackedSet = new HashSet<string>(trackedChatIds);

        var userSubscriptions = _subscriptions.GetOrAdd(
            subscriberId,
            _ => new ConcurrentDictionary<Guid, SubscriptionData>()
        );

        userSubscriptions[connectionId] = new SubscriptionData
        {
            Stream = responseStream,
            TrackedChatIds = trackedSet
        };

        AddToReverseIndex(connectionId, subscriberId, trackedSet, responseStream);

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
            RemoveFromReverseIndex(connectionId, removed.TrackedChatIds);
        }

        if (userSubscriptions.IsEmpty)
        {
            _subscriptions.TryRemove(subscriberId, out _);
        }
    }

    /// <summary>
    /// Получить стримы, отслеживающие данный чат, кроме самого печатающего.
    /// O(1) за счёт обратного индекса.
    /// </summary>
    public List<IServerStreamWriter<TypingEvent>> GetStreamsTrackingChat(string chatId, long exceptSubscriberId)
    {
        if (!_reverseIndex.TryGetValue(chatId, out var connections))
        {
            return [];
        }

        return connections.Values
            .Where(entry => entry.SubscriberId != exceptSubscriberId)
            .Select(entry => entry.Stream)
            .ToList();
    }

    /// <summary>
    /// Обновить TrackedChatIds во всех активных подписках пользователя.
    /// </summary>
    /// <returns>Количество обновлённых подписок (0 если нет активных)</returns>
    public int UpdateAllSubscriptions(long subscriberId, List<string> newChatIds)
    {
        if (!_subscriptions.TryGetValue(subscriberId, out var userSubscriptions))
        {
            return 0;
        }

        foreach (var (connectionId, oldSubscription) in userSubscriptions.ToList())
        {
            var newSet = new HashSet<string>(newChatIds);

            var newSubscription = new SubscriptionData
            {
                Stream = oldSubscription.Stream,
                TrackedChatIds = newSet
            };

            if (userSubscriptions.TryUpdate(connectionId, newSubscription, oldSubscription))
            {
                RemoveFromReverseIndex(connectionId, oldSubscription.TrackedChatIds);
                AddToReverseIndex(connectionId, subscriberId, newSet, oldSubscription.Stream);
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
    /// Количество уникальных отслеживаемых чатов (размер обратного индекса) — для метрик.
    /// </summary>
    public int GetTrackedUniqueChatsCount() => _reverseIndex.Count;

    private void AddToReverseIndex(
        Guid connectionId,
        long subscriberId,
        IEnumerable<string> trackedChatIds,
        IServerStreamWriter<TypingEvent> stream)
    {
        var entry = new ReverseEntry(stream, subscriberId);

        foreach (var chatId in trackedChatIds)
        {
            var connections = _reverseIndex.GetOrAdd(
                chatId,
                _ => new ConcurrentDictionary<Guid, ReverseEntry>());

            connections[connectionId] = entry;
        }
    }

    private void RemoveFromReverseIndex(Guid connectionId, IEnumerable<string> trackedChatIds)
    {
        foreach (var chatId in trackedChatIds)
        {
            if (_reverseIndex.TryGetValue(chatId, out var connections))
            {
                connections.TryRemove(connectionId, out _);

                if (connections.IsEmpty)
                {
                    _reverseIndex.TryRemove(chatId, out _);
                }
            }
        }
    }
}
