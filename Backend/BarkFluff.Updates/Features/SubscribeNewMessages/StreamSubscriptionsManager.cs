namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using System.Collections.Concurrent;
using BarkFluff.Proto.Updates;
using Grpc.Core;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, ConcurrentBag<IServerStreamWriter<NewMessageEvent>>> _userSubscriptions = new();

    public void RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        _userSubscriptions.AddOrUpdate(
            userId,
            new ConcurrentBag<IServerStreamWriter<NewMessageEvent>>(new[] { responseStream }),
            (_, bag) =>
            {
                bag.Add(responseStream);
                return bag;
            });
    }

    public void RemoveSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        if (_userSubscriptions.TryGetValue(userId, out var streams))
        {
            var updatedStreams = new ConcurrentBag<IServerStreamWriter<NewMessageEvent>>(
                streams.Where(s => s != responseStream));
            
            if (updatedStreams.IsEmpty)
            {
                _userSubscriptions.TryRemove(userId, out _);
            }
            else
            {
                _userSubscriptions[userId] = updatedStreams;
            }
        }
    }

    public IEnumerable<IServerStreamWriter<NewMessageEvent>> GetUserStreams(long userId)
    {
        return _userSubscriptions.TryGetValue(userId, out var streams) 
            ? streams 
            : Enumerable.Empty<IServerStreamWriter<NewMessageEvent>>();
    }
}
