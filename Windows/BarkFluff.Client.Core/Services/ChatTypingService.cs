using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Proto.Onliner;
using Grpc.Core;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class ChatTypingService : IChatTypingService, IDisposable
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    internal sealed record Session(object Connection, long UserId, Func<bool> IsCurrent);

    private sealed class ChatWatch(string chatId, Session session)
    {
        internal string ChatId { get; } = chatId;
        internal Session Session { get; } = session;
        internal CancellationTokenSource? Stream { get; set; }
        internal Dictionary<(long UserId, string Uuid), long> Typists { get; } = [];
    }

    private sealed class Activity(long started)
    {
        internal long LastInput { get; set; } = started;
        internal long? LastSent { get; set; }
        internal bool Pending { get; set; }
    }

    private readonly WebApiClient? _webApi;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly Func<Session?> _getSession;
    private readonly Func<string, CancellationToken, IAsyncEnumerable<TypingEvent>> _subscribe;
    private readonly Func<string, TypingAction, CancellationToken, Task> _send;
    private readonly ITimer _sendTimer;
    private readonly ITimer _expiryTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private ChatWatch? _watch;
    private Session? _unsupported;
    private long _tokenVersion;
    private Activity? _activity;
    private Task _pendingSend = Task.CompletedTask;

    public ChatTypingService(WebApiClient webApi, IClientSession session, IUiDispatcher dispatcher)
        : this(dispatcher, TimeProvider.System, () =>
        {
            var connection = session.CurrentConnection;
            if (connection is null || connection.ConnectionParameters.UserId <= 0)
            {
                return null;
            }

            var userId = connection.ConnectionParameters.UserId;
            return new Session(connection, userId, () => ReferenceEquals(connection, session.CurrentConnection)
                && connection.ConnectionParameters.UserId == userId);
        }, webApi.SubscribeToTypingWithStatus, webApi.SetTypingStatusOnceAsync)
    {
        _webApi = webApi;
        webApi.TokenRefreshed += OnTokenRefreshed;
    }

    internal ChatTypingService(IUiDispatcher dispatcher, TimeProvider clock, Func<Session?> getSession,
        Func<string, CancellationToken, IAsyncEnumerable<TypingEvent>> subscribe,
        Func<string, TypingAction, CancellationToken, Task> send)
    {
        _dispatcher = dispatcher;
        _clock = clock;
        _getSession = getSession;
        _subscribe = subscribe;
        _send = send;
        _sendTimer = CreateTimer(UpdateSending);
        _expiryTimer = CreateTimer(Expire);
    }

    public event Action? Changed;
    public IReadOnlyCollection<long> TypingUserIds => _watch is { } watch && watch.Session.IsCurrent()
        ? watch.Typists.Keys.Select(key => key.UserId).ToArray() : [];

    public void Watch(string? chatId)
    {
        StopSending();
        StopStream();
        _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var session = chatId is null ? null : _getSession();
        _watch = chatId is not null && session is not null ? new ChatWatch(chatId, session) : null;
        if (_watch is { } watch && !IsUnsupported(watch.Session))
        {
            StartStream(watch);
        }
        Changed?.Invoke();
    }

    public void Input(bool hasText)
    {
        if (!hasText)
        {
            StopSending();
            return;
        }

        if (_watch is not { } watch || !watch.Session.IsCurrent() || IsUnsupported(watch.Session))
        {
            return;
        }

        var now = _clock.GetTimestamp();
        if (_activity is { } current && _clock.GetElapsedTime(current.LastInput, now) > Idle)
        {
            _activity = null;
        }

        var start = _activity is null;
        var activity = _activity ??= new Activity(now);
        activity.LastInput = now;
        if (start)
        {
            QueueTyping(watch, activity);
        }
        if (ReferenceEquals(_activity, activity))
        {
            ScheduleSending(activity);
        }
    }

    private void QueueTyping(ChatWatch watch, Activity activity)
    {
        activity.Pending = true;
        _pendingSend = SendAsync(_pendingSend, watch, activity, TypingAction.Typing);
    }

    public void StopSending()
    {
        var activity = _activity;
        _activity = null;
        _sendTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (activity?.LastSent is not null && _watch is { } watch)
        {
            _pendingSend = SendAsync(_pendingSend, watch, activity, TypingAction.Cancelled);
        }
    }

    public Task StopAsync()
    {
        Watch(null);
        return _pendingSend;
    }

    private async Task SendAsync(Task previous, ChatWatch watch, Activity activity, TypingAction action)
    {
        // CANCELLED следует за уже начатым TYPING, в том числе при быстром возврате в тот же чат.
        await previous;
        if (_shutdown.IsCancellationRequested || !watch.Session.IsCurrent() || IsUnsupported(watch.Session))
        {
            return;
        }
        if (action == TypingAction.Typing)
        {
            activity.Pending = false;
            if (!ReferenceEquals(_activity, activity) || !ReferenceEquals(_watch, watch) ||
                _clock.GetElapsedTime(activity.LastInput) > Idle)
            {
                return;
            }
            activity.LastSent = _clock.GetTimestamp();
            ScheduleSending(activity);
        }

        var tokenVersion = _tokenVersion;
        try
        {
            await _send(watch.ChatId, action, _shutdown.Token);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            _dispatcher.Post(() =>
            {
                if (!_shutdown.IsCancellationRequested && tokenVersion == _tokenVersion && watch.Session.IsCurrent())
                {
                    MarkUnsupported(watch.Session);
                }
            });
        }
        catch (Exception)
        {
            // Ошибка отправки кратковременного индикатора не должна останавливать очередь.
        }
    }

    private void ScheduleSending(Activity activity)
    {
        var idleDue = Idle - _clock.GetElapsedTime(activity.LastInput);
        var sendDue = !activity.Pending && activity.LastSent is long sent
            ? Heartbeat - _clock.GetElapsedTime(sent) : idleDue;
        var due = sendDue < idleDue ? sendDue : idleDue;
        _sendTimer.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void UpdateSending()
    {
        if (_activity is not { } activity || _watch is not { } watch)
        {
            return;
        }

        // При совпадении границ Web отправляет повтор, если последнему вводу ровно 5 секунд.
        var idleFor = _clock.GetElapsedTime(activity.LastInput);
        if (!activity.Pending && idleFor <= Idle && activity.LastSent is long sent &&
            _clock.GetElapsedTime(sent) >= Heartbeat)
        {
            QueueTyping(watch, activity);
        }

        if (idleFor >= Idle || !watch.Session.IsCurrent())
        {
            _activity = null;
            _sendTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        else
        {
            ScheduleSending(activity);
        }
    }

    private void StartStream(ChatWatch watch)
    {
        var cancellation = watch.Stream = new CancellationTokenSource();
        _ = ListenAsync(watch, cancellation.Token);
    }

    private async Task ListenAsync(ChatWatch watch, CancellationToken token)
    {
        await StreamRetryLoop.RunAsync<TypingEvent>(
            ct => Task.FromResult<IAsyncEnumerable<TypingEvent>?>(watch.Session.IsCurrent()
                ? _subscribe(watch.ChatId, ct) : null),
            item =>
            {
                var received = _clock.GetTimestamp();
                _dispatcher.Post(() =>
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(watch, _watch) && watch.Session.IsCurrent())
                    {
                        Receive(watch, item, received);
                    }
                });
            },
            null, StreamRetryLoop.Backoff, token,
            delay: (delay, ct) => Task.Delay(delay, _clock, ct), stopOnUnimplemented: true).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            return;
        }
        _dispatcher.Post(() =>
        {
            if (!_shutdown.IsCancellationRequested && !token.IsCancellationRequested && watch.Session.IsCurrent())
            {
                MarkUnsupported(watch.Session);
            }
        });
    }

    private void Receive(ChatWatch watch, TypingEvent item, long received)
    {
        if (item.ChatId != watch.ChatId || item.UserId == watch.Session.UserId)
        {
            return;
        }

        var key = (item.UserId, item.UserId == 0 ? item.UserUuid : string.Empty);
        if (item.Action == TypingAction.Typing)
        {
            if (_clock.GetElapsedTime(received) >= Lifetime)
            {
                return;
            }
            watch.Typists[key] = received;
        }
        else if (item.Action != TypingAction.Cancelled || !watch.Typists.Remove(key))
        {
            return;
        }

        ScheduleExpiry(watch);
        Changed?.Invoke();
    }

    private void Expire()
    {
        if (_watch is not { } watch)
        {
            return;
        }

        var expired = watch.Typists.Where(pair => _clock.GetElapsedTime(pair.Value) >= Lifetime)
            .Select(pair => pair.Key).ToArray();
        foreach (var key in expired)
        {
            watch.Typists.Remove(key);
        }
        ScheduleExpiry(watch);
        if (expired.Length > 0)
        {
            Changed?.Invoke();
        }
    }

    private void ScheduleExpiry(ChatWatch watch)
    {
        var due = watch.Typists.Count == 0 ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromTicks(Math.Max(0, (Lifetime - _clock.GetElapsedTime(watch.Typists.Values.Min())).Ticks));
        _expiryTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private bool IsUnsupported(Session session) => _unsupported is { } unsupported &&
        ReferenceEquals(unsupported.Connection, session.Connection) && unsupported.UserId == session.UserId;

    private void MarkUnsupported(Session session)
    {
        _unsupported = session;
        if (_watch is { } watch && IsUnsupported(watch.Session))
        {
            StopSending();
            StopStream();
            watch.Typists.Clear();
            ScheduleExpiry(watch);
            Changed?.Invoke();
        }
    }

    private void StopStream()
    {
        if (_watch?.Stream is { } stream)
        {
            _watch.Stream = null;
            stream.Cancel();
            stream.Dispose();
        }
    }

    private void OnTokenRefreshed(object? sender, EventArgs args) => RefreshToken();

    internal void RefreshToken()
    {
        var session = _getSession();
        _dispatcher.Post(() =>
        {
            if (_shutdown.IsCancellationRequested || session is null || !session.IsCurrent())
            {
                return;
            }

            _tokenVersion++;
            if (IsUnsupported(session))
            {
                _unsupported = null;
            }
            if (_watch is { } watch && watch.Session.IsCurrent())
            {
                StopStream();
                StartStream(watch);
            }
        });
    }

    private ITimer CreateTimer(Action callback) => _clock.CreateTimer(
        _ => _dispatcher.Post(() =>
        {
            if (!_shutdown.IsCancellationRequested)
            {
                callback();
            }
        }), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        if (_webApi is not null)
        {
            _webApi.TokenRefreshed -= OnTokenRefreshed;
        }
        _shutdown.Cancel();
        StopStream();
        _sendTimer.Dispose();
        _expiryTimer.Dispose();
        _shutdown.Dispose();
    }
}
