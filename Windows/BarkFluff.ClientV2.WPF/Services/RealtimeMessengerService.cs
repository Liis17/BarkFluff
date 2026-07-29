using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class RealtimeMessengerService : IRealtimeMessengerService
{
    private readonly WebApiClient _webApi;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _messageReadsTask;
    private Task? _privateMessageReadsTask;
    private GlobalParam? _parameters;
    private bool _isDisposed;

    public RealtimeMessengerService(WebApiClient webApi)
    {
        _webApi = webApi;
        _webApi.TokenRefreshed += OnTokenRefreshed;
    }

    public event EventHandler<MessageReadReceipt>? MessageRead;

    public event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    public async Task StartAsync(GlobalParam parameters, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _parameters = parameters;
            _cancellationTokenSource = new CancellationTokenSource();
            _messageReadsTask = ListenMessageReadsAsync(parameters, _cancellationTokenSource.Token);
            _privateMessageReadsTask = ListenPrivateMessageReadsAsync(parameters, _cancellationTokenSource.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _webApi.TokenRefreshed -= OnTokenRefreshed;
        await StopAsync();
        _lifecycleLock.Dispose();
    }

    private async Task ListenMessageReadsAsync(GlobalParam parameters, CancellationToken cancellationToken)
    {
        var (error, stream) = await _webApi.SubscribeToReadReceipts(parameters, cancellationToken);
        if (!error.IsSuccess || stream is null)
        {
            return;
        }

        try
        {
            await foreach (var update in stream.WithCancellation(cancellationToken))
            {
                MessageRead?.Invoke(this, new MessageReadReceipt(update.ChatId, update.MessageId, update.NewReadBy.ToArray()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task ListenPrivateMessageReadsAsync(GlobalParam parameters, CancellationToken cancellationToken)
    {
        var (error, stream) = await _webApi.SubscribeToPrivateMessagesRead(parameters, cancellationToken);
        if (!error.IsSuccess || stream is null)
        {
            return;
        }

        try
        {
            await foreach (var update in stream.WithCancellation(cancellationToken))
            {
                PrivateMessageRead?.Invoke(this, new PrivateMessageReadReceipt(update.ChatId, update.UserId, update.LastReadMessageId));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task StopCoreAsync()
    {
        _parameters = null;
        var cancellation = _cancellationTokenSource;
        _cancellationTokenSource = null;
        cancellation?.Cancel();

        var tasks = new[] { _messageReadsTask, _privateMessageReadsTask }.Where(task => task is not null).Cast<Task>().ToArray();
        _messageReadsTask = null;
        _privateMessageReadsTask = null;
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    private void OnTokenRefreshed(object? sender, EventArgs eventArgs)
    {
        if (_parameters is { } parameters && !_isDisposed)
        {
            _ = RestartAfterTokenRefreshAsync(parameters);
        }
    }

    private async Task RestartAfterTokenRefreshAsync(GlobalParam parameters)
    {
        try
        {
            await StartAsync(parameters);
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
