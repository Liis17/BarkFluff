using System.Collections.Concurrent;

namespace BarkFluff.Calls.Services;

/// <summary>
/// Планировщик таймаута «не ответили» (Singleton). На InitiateCall ставим отложенную
/// задачу; AcceptCall/RejectCall/EndCall отменяют её. По срабатыванию звонок помечается
/// пропущенным (CallsService.TimeoutAsync). Аналог PendingPushTracker в Updates.
/// </summary>
public class CallTimeoutScheduler
{
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CallTimeoutScheduler> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    public CallTimeoutScheduler(IServiceScopeFactory scopeFactory, ILogger<CallTimeoutScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Schedule(Guid callId)
    {
        var cts = new CancellationTokenSource();
        _pending[callId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RingTimeout, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // приняли/отклонили/завершили раньше таймаута
            }
            finally
            {
                _pending.TryRemove(callId, out _);
                cts.Dispose();
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var calls = scope.ServiceProvider.GetRequiredService<CallsService>();
                await calls.TimeoutAsync(callId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка обработки таймаута звонка {CallId}", callId);
            }
        });
    }

    public void Cancel(Guid callId)
    {
        if (_pending.TryRemove(callId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
