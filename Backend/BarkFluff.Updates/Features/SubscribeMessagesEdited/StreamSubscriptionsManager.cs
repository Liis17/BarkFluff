namespace BarkFluff.Updates.Features.SubscribeMessagesEdited;

using BarkFluff.Proto.Updates;

using Grpc.Core;

using System.Collections.Concurrent;
using System.Threading;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, IServerStreamWriter<MessageEditedEvent>>> _userSubscriptions = new();
    private long _activeSubscriptionsCount;

    public long ActiveCount => Interlocked.Read(ref _activeSubscriptionsCount);

    public Guid RegisterSubscription(long userId, IServerStreamWriter<MessageEditedEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var userStreams = _userSubscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, IServerStreamWriter<MessageEditedEvent>>());
        userStreams[subscriptionId] = responseStream;
        Interlocked.Increment(ref _activeSubscriptionsCount);
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid subscriptionId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            if (userStreams.TryRemove(subscriptionId, out _))
            {
                Interlocked.Decrement(ref _activeSubscriptionsCount);
            }

            if (userStreams.IsEmpty)
            {
                _userSubscriptions.TryRemove(userId, out _);
            }
        }
    }

    public IEnumerable<IServerStreamWriter<MessageEditedEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            return userStreams.Values.ToList();
        }
        return [];
    }
}
