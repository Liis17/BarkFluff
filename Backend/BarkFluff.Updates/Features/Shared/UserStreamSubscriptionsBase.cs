namespace BarkFluff.Updates.Features.Shared;

using System.Collections.Concurrent;
using System.Threading;

using Grpc.Core;

/// <summary>
/// Базовый менеджер user-scope стримов: ключ — userId, на одного пользователя
/// допустимо несколько активных подписок (несколько устройств, несколько вкладок).
/// При публикации события рассылка идёт всем стримам этого userId.
/// </summary>
public abstract class UserStreamSubscriptionsBase<TEvent>
    where TEvent : class
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>> _userSubscriptions = new();
    private long _activeSubscriptionsCount;

    public long ActiveCount => Interlocked.Read(ref _activeSubscriptionsCount);

    public Guid RegisterSubscription(long userId, IServerStreamWriter<TEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var userStreams = _userSubscriptions.GetOrAdd(userId,
            _ => new ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>());
        userStreams[subscriptionId] = responseStream;
        Interlocked.Increment(ref _activeSubscriptionsCount);
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid subscriptionId)
    {
        if (!_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            return;
        }

        if (userStreams.TryRemove(subscriptionId, out _))
        {
            Interlocked.Decrement(ref _activeSubscriptionsCount);
        }

        if (userStreams.IsEmpty)
        {
            _userSubscriptions.TryRemove(userId, out _);
        }
    }

    public IEnumerable<IServerStreamWriter<TEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            return userStreams.Values.ToList();
        }

        return Array.Empty<IServerStreamWriter<TEvent>>();
    }
}
