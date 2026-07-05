using System.Collections.Concurrent;

namespace BarkFluff.Bots.Services;

/// <summary>
/// In-process сигнал «у бота появился новый update» для long-poll getUpdates и gRPC-стримов.
/// TaskCompletionSource на бота; при горизонтальном масштабировании переезжает в Redis pub/sub.
/// </summary>
public class BotUpdateNotifier
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _waiters = new();

    /// <summary>Сигнал: у бота botId появился новый update.</summary>
    public void Signal(long botId)
    {
        if (_waiters.TryRemove(botId, out var tcs))
            tcs.TrySetResult();
    }

    /// <summary>
    /// Ждать сигнала не дольше timeout. true = сигнал получен, false = таймаут/отмена.
    /// </summary>
    public async Task<bool> WaitForUpdateAsync(long botId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = _waiters.GetOrAdd(botId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        try
        {
            await tcs.Task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            // Убираем свой waiter, если он не был использован (иначе следующий вызов пересоздаст)
            _waiters.TryRemove(new KeyValuePair<long, TaskCompletionSource>(botId, tcs));
        }
    }
}
