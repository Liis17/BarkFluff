namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using BarkFluff.Proto.Updates;

using Grpc.Core;

using System.Collections.Concurrent;
using System.Threading;

public class StreamSubscriptionsManager
{
    // Поддержка нескольких клиентов на одного пользователя: userId -> (subscriptionId -> stream)
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, NewMessageStreamSubscription>> _userSubscriptions = new();
    private long _activeSubscriptionsCount;

    public long ActiveCount => Interlocked.Read(ref _activeSubscriptionsCount);

    public Guid RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var userStreams = _userSubscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, NewMessageStreamSubscription>());
        userStreams[subscriptionId] = new NewMessageStreamSubscription(responseStream);
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

            // Очищаем пустые записи пользователей
            if (userStreams.IsEmpty)
            {
                _userSubscriptions.TryRemove(userId, out _);
            }
        }
    }

    public NewMessageStreamSubscription GetSubscription(long userId, Guid subscriptionId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams)
            && userStreams.TryGetValue(subscriptionId, out var subscription))
            return subscription;

        throw new InvalidOperationException($"Subscription {subscriptionId} for user {userId} was not found");
    }

    public IEnumerable<NewMessageStreamSubscription> GetUserSubscriptions(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
            return userStreams.Values.ToList();

        return [];
    }

    public IEnumerable<IServerStreamWriter<NewMessageEvent>> GetUserStreams(long userId)
        => GetUserSubscriptions(userId).Select(subscription => subscription.Stream);
}

/// <summary>
/// Одна подписка на server-stream. gRPC допускает только одну активную запись
/// в конкретный stream, поэтому heartbeat и события проходят через общий lock.
/// </summary>
public sealed class NewMessageStreamSubscription
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public NewMessageStreamSubscription(IServerStreamWriter<NewMessageEvent> stream)
    {
        Stream = stream;
    }

    public IServerStreamWriter<NewMessageEvent> Stream { get; }

    public async Task WriteAsync(NewMessageEvent message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await Stream.WriteAsync(message);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
