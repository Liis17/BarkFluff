namespace BarkFluff.Client.Core.Infrastructure.Realtime;

/// <summary>Coalesces requests on the UI thread. A session owns one loop.</summary>
internal sealed class ResyncLoop(Func<CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken)
{
    private CancellationTokenSource? _confirmation;
    private bool _running;
    private bool _pending;
    private bool _online = true;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    internal void SetOnline(bool online)
    {
        _online = online;
        if (online && _pending)
        {
            RequestImmediate();
        }
    }

    internal void RequestConfirmation()
    {
        _confirmation?.Cancel();
        var confirmation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _confirmation = confirmation;
        _ = ConfirmAsync(confirmation);
    }

    internal void RequestImmediate()
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _pending = true;
        if (!_running && _online)
        {
            _running = true;
            _ = RunAsync();
        }
    }

    private async Task ConfirmAsync(CancellationTokenSource confirmation)
    {
        try
        {
            await Delay(TimeSpan.FromMilliseconds(750), confirmation.Token);
            if (!confirmation.IsCancellationRequested)
            {
                RequestImmediate();
            }
        }
        catch (OperationCanceledException) when (confirmation.IsCancellationRequested)
        {
        }
        finally
        {
            if (_confirmation == confirmation)
            {
                _confirmation = null;
            }

            confirmation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var attempt = 0;
            while (_pending && _online && !cancellationToken.IsCancellationRequested)
            {
                _pending = false;
                bool success;
                try
                {
                    success = await synchronize(cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    success = false;
                }

                if (success)
                {
                    attempt = 0;
                    continue;
                }

                _pending = true;
                if (_online)
                {
                    await Delay(StreamRetryLoop.Backoff(++attempt), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _running = false;
        }
    }
}
