using System.Collections.Concurrent;
using BarkFluff.Proto.Updates;
using Grpc.Core;

namespace BarkFluff.Updates.Features.SubscribeMessagesRead;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, ConcurrentBag<IServerStreamWriter<MessageReadEvent>>> _userSubscriptions = new();

    public void RegisterSubscription(long userId, IServerStreamWriter<MessageReadEvent> responseStream)
    {
        _userSubscriptions.AddOrUpdate(
            userId,
            new ConcurrentBag<IServerStreamWriter<MessageReadEvent>>(new[] { responseStream }),
            (_, bag) =>
            {
                bag.Add(responseStream);
                return bag;
            });
    }

    public void RemoveSubscription(long userId, IServerStreamWriter<MessageReadEvent> responseStream)
    {
        if (_userSubscriptions.TryGetValue(userId, out var streams))
        {
            var updatedStreams = new ConcurrentBag<IServerStreamWriter<MessageReadEvent>>(
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

    public IEnumerable<IServerStreamWriter<MessageReadEvent>> GetUserStreams(long userId)
    {
        return _userSubscriptions.TryGetValue(userId, out var streams) 
            ? streams 
            : Enumerable.Empty<IServerStreamWriter<MessageReadEvent>>();
    }
}
