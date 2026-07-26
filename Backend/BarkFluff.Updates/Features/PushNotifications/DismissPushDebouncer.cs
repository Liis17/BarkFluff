using System.Collections.Concurrent;

namespace BarkFluff.Updates.Features.PushNotifications;

public class DismissPushDebouncer
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<(long UserId, Guid ChatId), CancellationTokenSource> _pending = new();
    private readonly TimeSpan _delay;

    public DismissPushDebouncer() : this(DefaultDelay)
    {
    }

    public DismissPushDebouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task RunAsync(
        long userId,
        Guid chatId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var key = (userId, chatId);
        var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (true)
        {
            if (_pending.TryAdd(key, current))
                break;

            if (!_pending.TryGetValue(key, out var previous))
                continue;

            if (!_pending.TryUpdate(key, current, previous))
                continue;

            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The replaced invocation completed between lookup and cancellation.
            }
            break;
        }

        try
        {
            await Task.Delay(_delay, current.Token);
            await action(current.Token);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        finally
        {
            if (_pending.TryGetValue(key, out var pending) && ReferenceEquals(pending, current))
                _pending.TryRemove(key, out _);

            current.Dispose();
        }
    }
}
