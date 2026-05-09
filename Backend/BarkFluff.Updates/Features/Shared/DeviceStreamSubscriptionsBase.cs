namespace BarkFluff.Updates.Features.Shared;

using System.Collections.Concurrent;
using System.Threading;

using Grpc.Core;

/// <summary>
/// Базовый менеджер device-scope стримов: ключ — (userId, deviceId).
/// Используется для секретных чатов: события маршрутизируются именно на устройство-получателя.
/// На одну пару (userId, deviceId) допустимо несколько активных подписок (например,
/// клиент переподключился, не успев закрыть старый стрим).
/// </summary>
public abstract class DeviceStreamSubscriptionsBase<TEvent>
    where TEvent : class
{
    private readonly ConcurrentDictionary<DeviceKey, ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>> _deviceSubscriptions = new();
    private long _activeSubscriptionsCount;

    public long ActiveCount => Interlocked.Read(ref _activeSubscriptionsCount);

    public Guid RegisterSubscription(long userId, Guid deviceId, IServerStreamWriter<TEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var key = new DeviceKey(userId, deviceId);
        var deviceStreams = _deviceSubscriptions.GetOrAdd(key,
            _ => new ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>());
        deviceStreams[subscriptionId] = responseStream;
        Interlocked.Increment(ref _activeSubscriptionsCount);
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid deviceId, Guid subscriptionId)
    {
        var key = new DeviceKey(userId, deviceId);
        if (!_deviceSubscriptions.TryGetValue(key, out var deviceStreams))
        {
            return;
        }

        if (deviceStreams.TryRemove(subscriptionId, out _))
        {
            Interlocked.Decrement(ref _activeSubscriptionsCount);
        }

        if (deviceStreams.IsEmpty)
        {
            _deviceSubscriptions.TryRemove(key, out _);
        }
    }

    public IEnumerable<IServerStreamWriter<TEvent>> GetDeviceStreams(long userId, Guid deviceId)
    {
        var key = new DeviceKey(userId, deviceId);
        if (_deviceSubscriptions.TryGetValue(key, out var deviceStreams))
        {
            return deviceStreams.Values.ToList();
        }

        return Array.Empty<IServerStreamWriter<TEvent>>();
    }

    public bool HasActiveStreams(long userId, Guid deviceId)
    {
        var key = new DeviceKey(userId, deviceId);
        return _deviceSubscriptions.TryGetValue(key, out var deviceStreams) && !deviceStreams.IsEmpty;
    }

    private readonly record struct DeviceKey(long UserId, Guid DeviceId);
}
