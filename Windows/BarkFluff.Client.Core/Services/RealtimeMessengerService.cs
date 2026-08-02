using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class RealtimeMessengerService : IRealtimeMessengerService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private static readonly TimeSpan StreamStopTimeout = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _newMessagesTask;
    private Task? _messageReadsTask;
    private Task? _privateMessageReadsTask;
    private bool _isDisposed;

    public RealtimeMessengerService(WebApiClient webApi, IClientSession session)
    {
        _webApi = webApi;
        _session = session;
        _webApi.TokenRefreshed += OnTokenRefreshed;
    }

    public event EventHandler<IncomingMessage>? MessageReceived;

    public event EventHandler<MessageReadReceipt>? MessageRead;

    public event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    public event EventHandler<bool>? ConnectionChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_session.CurrentConnection?.ConnectionParameters is not { } parameters)
        {
            return;
        }
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _cancellationTokenSource = new CancellationTokenSource();
            _newMessagesTask = ListenNewMessagesAsync(parameters, _cancellationTokenSource.Token);
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

    private Task ListenNewMessagesAsync(GlobalParam parameters, CancellationToken cancellationToken) =>
        StreamRetryLoop.RunAsync(
            async token =>
            {
                var (error, stream) = await _webApi.JustUpdate(parameters, token);
                return error.IsSuccess ? stream : null;
            },
            update =>
            {
                if (update.Message is not null)
                {
                    MessageReceived?.Invoke(this, new IncomingMessage(update.ChatId, WebApiClient.MapEventMessage(update.Message, update.ChatId)));
                }
            },
            // Все три стрима идут одним каналом UpdatesAC и рвутся вместе, поэтому о связи
            // сообщает только этот цикл: три источника одного и того же состояния не нужны.
            isConnected => ConnectionChanged?.Invoke(this, isConnected),
            StreamRetryLoop.Backoff,
            cancellationToken);

    private Task ListenMessageReadsAsync(GlobalParam parameters, CancellationToken cancellationToken) =>
        StreamRetryLoop.RunAsync(
            async token =>
            {
                var (error, stream) = await _webApi.SubscribeToReadReceipts(parameters, token);
                return error.IsSuccess ? stream : null;
            },
            update => MessageRead?.Invoke(this, new MessageReadReceipt(update.ChatId, update.MessageId, update.NewReadBy.ToArray())),
            onConnectedChanged: null,
            StreamRetryLoop.Backoff,
            cancellationToken);

    private Task ListenPrivateMessageReadsAsync(GlobalParam parameters, CancellationToken cancellationToken) =>
        StreamRetryLoop.RunAsync(
            async token =>
            {
                var (error, stream) = await _webApi.SubscribeToPrivateMessagesRead(parameters, token);
                return error.IsSuccess ? stream : null;
            },
            update => PrivateMessageRead?.Invoke(this, new PrivateMessageReadReceipt(update.ChatId, update.UserId, update.LastReadMessageId)),
            onConnectedChanged: null,
            StreamRetryLoop.Backoff,
            cancellationToken);

    private async Task StopCoreAsync()
    {
        var cancellation = _cancellationTokenSource;
        _cancellationTokenSource = null;
        cancellation?.Cancel();

        var tasks = new[] { _newMessagesTask, _messageReadsTask, _privateMessageReadsTask }.Where(task => task is not null).Cast<Task>().ToArray();
        _newMessagesTask = null;
        _messageReadsTask = null;
        _privateMessageReadsTask = null;
        if (tasks.Length > 0)
        {
            // Читатели стримов кооперативные, но gRPC-вызов не всегда отпускает по отмене.
            // Ждать их бесконечно нельзя: этот метод вызывается при завершении приложения,
            // и зависшее ожидание не давало процессу закрыться.
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(StreamStopTimeout));
        }

        cancellation?.Dispose();
    }

    private void OnTokenRefreshed(object? sender, EventArgs eventArgs)
    {
        if (!_isDisposed)
        {
            _ = RestartAfterTokenRefreshAsync();
        }
    }

    private async Task RestartAfterTokenRefreshAsync()
    {
        try
        {
            await StartAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
