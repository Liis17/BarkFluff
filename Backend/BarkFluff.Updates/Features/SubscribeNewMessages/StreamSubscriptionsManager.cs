namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using System.Collections.Concurrent;
using BarkFluff.Proto.Updates;
using Grpc.Core;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, HashSet<IServerStreamWriter<NewMessageEvent>>> _userSubscriptions = new();
    private readonly object _lock = new();

    public void RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        lock (_lock)
        {
            if (!_userSubscriptions.TryGetValue(userId, out var streams))
            {
                streams = new HashSet<IServerStreamWriter<NewMessageEvent>>();
                _userSubscriptions[userId] = streams;
            }
            streams.Add(responseStream);
        }
    }

    public void RemoveSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        lock (_lock)
        {
            if (_userSubscriptions.TryGetValue(userId, out var streams))
            {
                streams.Remove(responseStream);
                
                if (streams.Count == 0)
                {
                    _userSubscriptions.TryRemove(userId, out _);
                }
            }
        }
    }

    public IEnumerable<IServerStreamWriter<NewMessageEvent>> GetUserStreams(long userId)
    {
        lock (_lock)
        {
            if (_userSubscriptions.TryGetValue(userId, out var streams))
            {
                // Return a copy of the list to avoid collection modification during enumeration
                return streams.ToList();
            }
            return Enumerable.Empty<IServerStreamWriter<NewMessageEvent>>();
        }
    }
}
