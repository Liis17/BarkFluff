using System.Collections.Concurrent;

namespace BarkFluff.Updates.Features.PushNotifications;

public class DismissPushDebouncer
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private sealed record Pending(CancellationTokenSource Cancellation, long MessageId);

    private readonly ConcurrentDictionary<(long UserId, Guid ChatId), Pending> _pending = new();
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
        long messageId,
        Func<long, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var key = (userId, chatId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var current = new Pending(cancellation, messageId);

        while (true)
        {
            if (_pending.TryAdd(key, current))
                break;

            if (!_pending.TryGetValue(key, out var previous))
                continue;

            current = current with { MessageId = Math.Max(current.MessageId, previous.MessageId) };
            if (!_pending.TryUpdate(key, current, previous))
                continue;

            try
            {
                previous.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The replaced invocation completed between lookup and cancellation.
            }
            break;
        }

        try
        {
            await Task.Delay(_delay, cancellation.Token);
            await action(current.MessageId, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<(long UserId, Guid ChatId), Pending>(key, current));
        }
    }
}
