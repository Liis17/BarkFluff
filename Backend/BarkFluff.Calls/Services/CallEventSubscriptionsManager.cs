using System.Collections.Concurrent;

using BarkFluff.Proto.Calls;

using Grpc.Core;

namespace BarkFluff.Calls.Services;

/// <summary>
/// Менеджер device-scope подписок на события звонков (как в Updates), Singleton.
/// Индексация по userId с привязкой к deviceId, чтобы:
///  • ринговать ВСЕ устройства получателя;
///  • гасить ринг на ОСТАЛЬНЫХ устройствах при ответе с одного;
///  • рассылать ринг всем участникам группового звонка.
/// Запись в один поток сериализована (gRPC stream не потокобезопасен на запись).
/// </summary>
public class CallEventSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, Subscription>> _byUser = new();
    private long _activeCount;

    public long ActiveCount => Interlocked.Read(ref _activeCount);

    public Guid RegisterSubscription(long userId, Guid deviceId, IServerStreamWriter<CallEvent> stream)
    {
        var subscriptionId = Guid.NewGuid();
        var subs = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Subscription>());
        subs[subscriptionId] = new Subscription(deviceId, stream);
        Interlocked.Increment(ref _activeCount);
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid subscriptionId)
    {
        if (!_byUser.TryGetValue(userId, out var subs))
        {
            return;
        }

        if (subs.TryRemove(subscriptionId, out _))
        {
            Interlocked.Decrement(ref _activeCount);
        }

        if (subs.IsEmpty)
        {
            _byUser.TryRemove(userId, out _);
        }
    }

    /// <summary>Отправить событие на все устройства пользователя (ring / уведомление caller).</summary>
    public Task SendToUserAsync(long userId, CallEvent evt)
        => FanOutAsync(GetUserSubscriptions(userId), evt);

    /// <summary>Отправить на все устройства пользователя, кроме одного (гасим ring на остальных).</summary>
    public Task SendToUserExceptDeviceAsync(long userId, Guid exceptDeviceId, CallEvent evt)
        => FanOutAsync(GetUserSubscriptions(userId).Where(s => s.DeviceId != exceptDeviceId), evt);

    /// <summary>Отправить событие группе пользователей (ring участникам группового звонка).</summary>
    public Task SendToUsersAsync(IEnumerable<long> userIds, CallEvent evt)
        => FanOutAsync(userIds.Distinct().SelectMany(GetUserSubscriptions), evt);

    private IEnumerable<Subscription> GetUserSubscriptions(long userId)
        => _byUser.TryGetValue(userId, out var subs) ? subs.Values.ToList() : Enumerable.Empty<Subscription>();

    private static async Task FanOutAsync(IEnumerable<Subscription> subscriptions, CallEvent evt)
    {
        var tasks = subscriptions.Select(sub => sub.WriteAsync(evt)).ToArray();
        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private sealed class Subscription
    {
        private readonly IServerStreamWriter<CallEvent> _stream;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public Guid DeviceId { get; }

        public Subscription(Guid deviceId, IServerStreamWriter<CallEvent> stream)
        {
            DeviceId = deviceId;
            _stream = stream;
        }

        public async Task WriteAsync(CallEvent evt)
        {
            await _gate.WaitAsync();
            try
            {
                await _stream.WriteAsync(evt);
            }
            catch
            {
                // Стрим оборвался — подписка снимется в finally gRPC-метода при отключении.
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
