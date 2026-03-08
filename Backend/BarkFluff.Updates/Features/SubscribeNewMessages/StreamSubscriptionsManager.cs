namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using BarkFluff.Proto.Updates;

using Grpc.Core;

using System.Collections.Concurrent;

public class StreamSubscriptionsManager
{
    // Поддержка нескольких клиентов на одного пользователя: userId -> (subscriptionId -> stream)
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>> _userSubscriptions = new();

    public Guid RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var userStreams = _userSubscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>());
        userStreams[subscriptionId] = responseStream;
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid subscriptionId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            userStreams.TryRemove(subscriptionId, out _);

            // Очищаем пустые записи пользователей
            if (userStreams.IsEmpty)
            {
                _userSubscriptions.TryRemove(userId, out _);
            }
        }
    }

    public IEnumerable<IServerStreamWriter<NewMessageEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            return userStreams.Values.ToList();
        }
        return [];
    }
}
