using BarkFluff.Proto.Updates;
using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class RealtimeMessengerService : IRealtimeMessengerService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private GlobalParam? _parameters;
    private Task[] _streams = [];
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
    public event EventHandler<MessageUpdate>? MessageChanged;
    public event EventHandler<bool>? ResyncRequested;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            if (!_isDisposed && _session.CurrentConnection?.ConnectionParameters is { } parameters)
            {
                StartCore(parameters);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void StartCore(GlobalParam parameters)
    {
        _parameters = parameters;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        bool IsCurrent() => !token.IsCancellationRequested &&
            ReferenceEquals(_session.CurrentConnection?.ConnectionParameters, parameters);

        _streams =
        [
            ListenForNewMessagesAsync(ct => _webApi.JustUpdate(parameters, ct), IsCurrent, token),
            Listen(_webApi.SubscribeToReadReceipts, e => MessageRead?.Invoke(this, new(e.ChatId, e.MessageId, e.NewReadBy.ToArray()))),
            Listen(_webApi.SubscribeToPrivateMessagesRead, e => PrivateMessageRead?.Invoke(this, new(e.ChatId, e.UserId, e.LastReadMessageId))),
            Listen(_webApi.SubscribeToMessagesEdited, e =>
            {
                if (e.Message is not null)
                {
                    Publish(new(e.ChatId, false, e.Message.Id, MessageUpdateKind.Edit, Message: WebApiClient.MapEventMessage(e.Message, e.ChatId)));
                }
            }),
            Listen(_webApi.SubscribeToMessagesDeleted, e => Publish(new(e.ChatId, false, e.MessageId, MessageUpdateKind.Delete))),
            Listen(_webApi.SubscribeToMessagesPinned, e => Publish(new(e.ChatId, false, e.MessageId, MessageUpdateKind.Pin, PinnedAt: e.PinnedAt, PinnerId: e.PinnerUserId))),
            Listen(_webApi.SubscribeToMessagesUnpinned, e => Publish(new(e.ChatId, false, e.MessageId, MessageUpdateKind.Unpin))),
            Listen(_webApi.SubscribeToAllMessagesUnpinned, e => Publish(new(e.ChatId, false, 0, MessageUpdateKind.UnpinAll))),
            Listen(_webApi.SubscribeToPrivateMessages, e =>
            {
                if (e.Message is not null)
                {
                    Publish(new(e.ChatId, true, e.Message.Id, MessageUpdateKind.New, Encrypted: e.Message));
                }
            }),
            Listen(_webApi.SubscribeToPrivateMessageEdits, e =>
            {
                if (e.Message is not null)
                {
                    Publish(new(e.ChatId, true, e.Message.Id, MessageUpdateKind.Edit, Encrypted: e.Message));
                }
            }),
            Listen(_webApi.SubscribeToPrivateMessageDeletes, e => Publish(new(e.ChatId, true, e.MessageId, MessageUpdateKind.Delete)))
        ];

        void Publish(MessageUpdate update) => MessageChanged?.Invoke(this, update);

        Task Listen<T>(Func<GlobalParam, CancellationToken, Task<(ErrorReturner, IAsyncEnumerable<T>?)>> subscribe,
            Action<T> receive) => ListenAsync(ct => subscribe(parameters, ct), receive, IsCurrent, token);
    }

    // Все потоки используют UpdatesAC. Состояние связи сообщает только поток новых сообщений.
    internal Task ListenForNewMessagesAsync(
        Func<CancellationToken, Task<(ErrorReturner, IAsyncEnumerable<NewMessageEvent>?)>> subscribe,
        Func<bool> isCurrent, CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        ListenAsync(subscribe, item =>
        {
            if (item.Message is not null)
            {
                MessageReceived?.Invoke(this, new(item.ChatId, WebApiClient.MapEventMessage(item.Message, item.ChatId)));
            }
        }, isCurrent, token, primary: true, delay);

    internal Task ListenAsync<T>(Func<CancellationToken, Task<(ErrorReturner, IAsyncEnumerable<T>?)>> subscribe,
        Action<T> receive, Func<bool> isCurrent, CancellationToken token, bool primary = false,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var first = false;
        return StreamRetryLoop.RunAsync(
            async ct =>
            {
                var (error, stream) = await subscribe(ct);
                return error.IsSuccess ? stream : null;
            },
            item =>
            {
                if (!isCurrent())
                {
                    return;
                }

                if (primary && first)
                {
                    first = false;
                    ResyncRequested?.Invoke(this, true);
                }

                if (isCurrent())
                {
                    receive(item);
                }
            },
            primary ? opened =>
            {
                first = opened;
                if (isCurrent())
                {
                    ConnectionChanged?.Invoke(this, opened);
                }
            } : null,
            StreamRetryLoop.Backoff,
            token,
            onStreamOpened: () =>
            {
                if (isCurrent())
                {
                    ResyncRequested?.Invoke(this, false);
                }
            },
            delay: delay);
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
        // Уже поставленные в очередь Start/Stop и TokenRefreshed ещё могут выйти из
        // ожидания и вызвать Release. SemaphoreSlim не открывает системный дескриптор.
    }

    private async Task StopCoreAsync()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        _parameters = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        // Читатели стримов кооперативные, но gRPC-вызов не всегда отпускает по отмене.
        // Ограничиваем ожидание, чтобы зависший поток не мешал завершению приложения.
        await Task.WhenAny(Task.WhenAll(_streams), Task.Delay(TimeSpan.FromSeconds(1)));
        _streams = [];
        cancellation.Dispose();
    }

    private void OnTokenRefreshed(object? sender, EventArgs args)
    {
        if (_cancellation is { } active)
        {
            _ = RestartAfterTokenRefreshAsync(active);
        }
    }

    private async Task RestartAfterTokenRefreshAsync(CancellationTokenSource active)
    {
        // Token refresh can be raised synchronously by a subscription while Start holds the lock.
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_isDisposed || _cancellation != active || _parameters is not { } parameters ||
                !ReferenceEquals(_session.CurrentConnection?.ConnectionParameters, parameters))
            {
                return;
            }

            await StopCoreAsync();
            StartCore(parameters);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }
}
