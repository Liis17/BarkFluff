namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using BarkFluff.Proto.Updates;

using Grpc.Core;

using System.Collections.Concurrent;

public class StreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, IServerStreamWriter<NewMessageEvent>> _userSubscriptions = new();

    public void RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        // Заменяем старую подписку новой (один пользователь = одна подписка)
        _userSubscriptions[userId] = responseStream;
    }

    public void RemoveSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        // Удаляем только если это тот же stream
        _userSubscriptions.TryRemove(new KeyValuePair<long, IServerStreamWriter<NewMessageEvent>>(userId, responseStream));
    }

    public IEnumerable<IServerStreamWriter<NewMessageEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var stream))
        {
            return [stream];
        }
        return [];
    }
}
