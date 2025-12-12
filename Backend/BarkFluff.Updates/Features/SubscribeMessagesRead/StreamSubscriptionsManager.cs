using BarkFluff.Proto.Updates;

using Grpc.Core;

using System.Collections.Concurrent;

namespace BarkFluff.Updates.Features.SubscribeMessagesRead;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, IServerStreamWriter<MessageReadEvent>> _userSubscriptions = new();

    public void RegisterSubscription(long userId, IServerStreamWriter<MessageReadEvent> responseStream)
    {
        // Заменяем старую подписку новой (один пользователь = одна подписка)
        _userSubscriptions[userId] = responseStream;
    }

    public void RemoveSubscription(long userId, IServerStreamWriter<MessageReadEvent> responseStream)
    {
        // Удаляем только если это тот же stream
        _userSubscriptions.TryRemove(new KeyValuePair<long, IServerStreamWriter<MessageReadEvent>>(userId, responseStream));
    }

    public IEnumerable<IServerStreamWriter<MessageReadEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var stream))
        {
            return [stream];
        }
        return [];
    }
}
