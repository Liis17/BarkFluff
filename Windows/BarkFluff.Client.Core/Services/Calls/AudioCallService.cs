using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Proto.Calls;
using BarkFluff.Shared.Exceptions.Users;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services.Calls;

public enum AudioCallPhase { Outgoing, Incoming, Connecting, Active }

public sealed record AudioCallStatus(Guid Id, long PeerId, string? PeerName, AudioCallPhase Phase,
    DateTimeOffset? ActiveSince = null, bool Muted = false, IReadOnlyList<CallAudioDevice>? Devices = null);

public sealed class AudioCallService(ICallMedia media, IUiDispatcher dispatcher, TimeProvider clock)
{
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(50);
    // Как в Web: watchdog обнаруживает зависший поток, а предел возраста обновляет его до таймаута прокси.
    private static readonly TimeSpan StreamSilence = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StreamMaxAge = TimeSpan.FromSeconds(180);

    private sealed class Session(ICallSignaling signaling)
    {
        public readonly ICallSignaling Signaling = signaling;
        public readonly CancellationTokenSource Lifetime = new();
        public readonly Dictionary<Guid, CallEvent> Early = [];
        public readonly Dictionary<Guid, DateTimeOffset> Closed = [];
        public CancellationTokenSource? Stream;
        public EventHandler? Refresh;
        public bool Unsupported;
    }

    private sealed class Call(AudioCallStatus status)
    {
        public AudioCallStatus Status = status;
        public readonly bool IsIncoming = status.Phase == AudioCallPhase.Incoming;
        public InitiateCallResponse? Offer;
        public ITimer? Timeout;
        public bool Connected;
        public bool PeerJoined;
    }

    private Session? _session;
    private Call? _call;
    public AudioCallStatus? Current => _call?.Status;
    public string? Outcome { get; private set; }
    public bool Available { get; private set; }
    public event Action? Changed;

    private bool IsCurrent(Session session) => _session == session && session.Signaling.IsCurrent;
    private bool IsCurrent(Session session, Call call) => IsCurrent(session) && _call == call;

    public void Start(ICallSignaling? signaling)
    {
        if (_session?.Signaling.IsCurrent == true)
            return;
        Stop();
        if (signaling?.IsAdvertised != true)
            return;

        var session = _session = new Session(signaling);
        session.Refresh = (_, _) => dispatcher.Post(() =>
        {
            if (IsCurrent(session) && !session.Unsupported)
                Listen(session);
        });
        signaling.TokenRefreshed += session.Refresh;
        media.Received += OnMediaReceived;
        Listen(session);
    }

    public void Stop() => _ = StopAsync();

    public async Task StopAsync()
    {
        Task finish = Task.CompletedTask;
        if (_session is { } session)
        {
            if (_call is { } call)
                finish = CloseAsync(session, call, call.Status.Phase == AudioCallPhase.Incoming, stopping: true);
            _session = null;
            session.Signaling.TokenRefreshed -= session.Refresh;
            media.Received -= OnMediaReceived;
            session.Stream?.Cancel();
            session.Stream?.Dispose();
            session.Lifetime.Cancel();
            session.Lifetime.Dispose();
        }
        Available = false;
        Outcome = null;
        Changed?.Invoke();
        await finish;
    }

    private void Listen(Session session)
    {
        session.Stream?.Cancel();
        session.Stream?.Dispose();
        session.Stream = new CancellationTokenSource();
        var ct = session.Stream.Token;

        void Apply(Action action) => dispatcher.Post(() =>
        {
            if (!ct.IsCancellationRequested && IsCurrent(session))
                action();
        });

        void Ready()
        {
            if (!Available)
            {
                Available = true;
                Changed?.Invoke();
            }
        }

        _ = RunAsync();
        async Task RunAsync()
        {
            long? openedAt = null;
            long lastActivity = 0;
            Action? checkStream = null;
            using var watchdog = clock.CreateTimer(_ => Apply(() => checkStream?.Invoke()), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            checkStream = () =>
            {
                if (openedAt is not { } start)
                    return;

                var ageLeft = StreamMaxAge - clock.GetElapsedTime(start);
                var silenceLeft = StreamSilence - clock.GetElapsedTime(lastActivity);
                var next = ageLeft < silenceLeft ? ageLeft : silenceLeft;
                if (next <= TimeSpan.Zero)
                    Listen(session);
                else
                    watchdog.Change(next, Timeout.InfiniteTimeSpan);
            };

            await StreamRetryLoop.RunAsync(ct =>
                {
                    Apply(() =>
                    {
                        lastActivity = clock.GetTimestamp();
                        openedAt = lastActivity;
                        watchdog.Change(StreamSilence, Timeout.InfiniteTimeSpan);
                    });
                    return session.Signaling.SubscribeAsync(ct);
                },
                item => Apply(() =>
                {
                    lastActivity = clock.GetTimestamp();
                    Ready();
                    Receive(session, item);
                }),
                connected =>
                {
                    if (!connected)
                        Apply(() =>
                        {
                            openedAt = null;
                            watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                            Offline();
                        });
                },
                StreamRetryLoop.Backoff, ct, onStreamOpened: () => Apply(Ready), stopOnUnimplemented: true);
            Apply(() =>
            {
                session.Unsupported = true;
                Offline("Calls_Unavailable");
            });
        }
    }

    private void Offline(string? outcome = null)
    {
        Available = false;
        if (outcome is not null)
            Outcome = outcome;
        Changed?.Invoke();
    }

    public async Task InitiateAsync(long peerId, string peerName)
    {
        if (_session is not { } session || !IsCurrent(session) || !Available || _call is not null)
            return;

        var call = _call = new Call(new(Guid.Empty, peerId, peerName, AudioCallPhase.Outgoing));
        Outcome = null;
        Changed?.Invoke();
        try
        {
            var offer = await session.Signaling.InitiateAsync(peerId, session.Lifetime.Token);
            if (!IsCurrent(session))
                return;
            if (offer is null || !Guid.TryParse(offer.CallId, out var id) || id == Guid.Empty)
                throw new InvalidOperationException();
            if (_call != call)
            {
                await FinishAsync(session, id, reject: false);
                return;
            }

            call.Offer = offer;
            call.Status = call.Status with { Id = id };
            SetTimeout(session, call, RingTimeout, "Calls_Missed");
            Changed?.Invoke();
            if (session.Early.Remove(id, out var early))
                Receive(session, early);
        }
        catch (Exception ex)
        {
            if (IsCurrent(session, call))
                HandleFailure(session, call, ex);
        }
        finally
        {
            if (_call == call || _call?.Status.Phase != AudioCallPhase.Outgoing)
                session.Early.Clear();
        }
    }

    public async Task AcceptAsync()
    {
        if (_session is not { } session || _call is not { Status.Phase: AudioCallPhase.Incoming } call ||
            !IsCurrent(session))
            return;

        var id = call.Status.Id;
        BeginConnecting(session, call);
        try
        {
            var answer = await session.Signaling.AcceptAsync(id.ToString("D"), session.Lifetime.Token);
            if (IsCurrent(session, call))
                await ConnectAsync(session, call, answer?.LivekitUrl, answer?.AccessToken);
        }
        catch (Exception ex)
        {
            if (IsCurrent(session, call))
                HandleFailure(session, call, ex);
        }
    }

    public Task HangUpAsync() => _session is { } session && _call is { } call
        ? CloseAsync(session, call, call.Status.Phase == AudioCallPhase.Incoming)
        : Task.CompletedTask;

    public void DismissOutcome()
    {
        Outcome = null;
        Changed?.Invoke();
    }

    public void ToggleMute()
    {
        if (_call is not { Status.Phase: AudioCallPhase.Active } call)
            return;
        var muted = !call.Status.Muted;
        media.Send(call.Status.Id, muted ? "mute" : "unmute");
        if (_call == call)
        {
            call.Status = call.Status with { Muted = muted };
            Changed?.Invoke();
        }
    }

    public void SelectDevice(CallAudioDevice device)
    {
        if (_call is { Status.Phase: AudioCallPhase.Active, Status.Devices: { } devices } call && devices.Contains(device))
            media.Send(call.Status.Id, device.Kind, device.Id);
    }

    public void SetPeerName(Guid callId, string name)
    {
        if (_call is { } call && call.Status.Id == callId && name.Length != 0)
        {
            call.Status = call.Status with { PeerName = name };
            Changed?.Invoke();
        }
    }

    private void Receive(Session session, CallEvent item)
    {
        if (item.Incoming is { } incoming)
        {
            var remaining = incoming.StartedAt is { } started
                ? started.ToDateTimeOffset() + RingTimeout - clock.GetUtcNow() : RingTimeout;
            if (incoming.ChatId.Length != 0 || incoming.MediaType != CallMediaType.CallMediaAudio ||
                incoming.CallerUserId <= 0 || !Guid.TryParse(incoming.CallId, out var incomingId) ||
                incomingId == Guid.Empty || IsClosed(session, incomingId))
                return;

            if (_call?.Status.Id == incomingId)
                return;
            if (_call is not null)
            {
                RememberClosed(session, incomingId);
                _ = FinishAsync(session, incomingId, reject: true);
                return;
            }

            var incomingCall = _call = new Call(new(incomingId, incoming.CallerUserId, null, AudioCallPhase.Incoming));
            Outcome = null;
            SetTimeout(session, incomingCall, remaining <= TimeSpan.Zero || remaining > RingTimeout
                ? RingTimeout : remaining, "Calls_Missed");
            Changed?.Invoke();
            return;
        }

        var rawId = item.Accepted?.CallId ?? item.Rejected?.CallId ?? item.Ended?.CallId;
        if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty || IsClosed(session, id))
            return;
        if (_call is not { } call || id != call.Status.Id)
        {
            if (_call?.Status.Id == Guid.Empty && (session.Early.ContainsKey(id) || session.Early.Count < 64) &&
                (!session.Early.TryGetValue(id, out var old) || old.Accepted is not null))
                session.Early[id] = item;
            if (_call?.Status.Id != Guid.Empty)
                RememberClosed(session, id);
            return;
        }

        if (item.Ended is { } ended)
            Clear(session, call, ended.Reason switch
            {
                CallEndReason.CallEndBusy => "Calls_Busy",
                CallEndReason.CallEndMissed => "Calls_Missed",
                CallEndReason.CallEndRejected => "Calls_Rejected",
                CallEndReason.CallEndFailed => "Calls_Failed",
                _ => "Calls_Ended"
            });
        else if (item.Rejected is not null)
            Clear(session, call, "Calls_Rejected");
        // Сервер исключает устройство, которое отправило AcceptCall, из рассылки.
        else if (item.Accepted is not null && call.IsIncoming)
            Clear(session, call, "Calls_AnsweredElsewhere");
        else if (item.Accepted is not null && call.Status.Phase == AudioCallPhase.Outgoing && call.Offer is { } offer)
        {
            BeginConnecting(session, call);
            _ = ConnectAsync(session, call, offer.LivekitUrl, offer.AccessToken);
        }
    }

    private void BeginConnecting(Session session, Call call)
    {
        call.Offer = null;
        call.Status = call.Status with { Phase = AudioCallPhase.Connecting };
        SetTimeout(session, call, TimeSpan.FromSeconds(25), "Calls_MediaError");
        Changed?.Invoke();
    }

    private async Task ConnectAsync(Session session, Call call, string? endpoint, string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || !CallMediaBridge.TryGetEndpoint(endpoint ?? string.Empty, out var url))
                throw new InvalidOperationException();
            await media.ConnectAsync(call.Status.Id, url, token, session.Lifetime.Token);
        }
        catch (Exception ex)
        {
            if (IsCurrent(session, call))
                _ = CloseAsync(session, call, false, ex is CallRuntimeException ? "Calls_Runtime" : "Calls_MediaError");
        }
    }

    private void OnMediaReceived(CallMediaEvent message) => dispatcher.Post(() =>
    {
        if (_session is not { } session || _call is not { } call || !IsCurrent(session) ||
            message.CallId != call.Status.Id || call.Status.Phase is not (AudioCallPhase.Connecting or AudioCallPhase.Active))
            return;
        switch (message.Type)
        {
            case "error":
            case "disconnected":
                _ = CloseAsync(session, call, false, "Calls_MediaError");
                return;
            case "participant_left":
                _ = CloseAsync(session, call, false, "Calls_Ended");
                return;
            case "connected":
                call.Connected = true;
                break;
            case "participant_joined":
                call.PeerJoined = true;
                break;
            case "devices":
                call.Status = call.Status with { Devices = message.Devices };
                break;
        }

        if (call.Status.Phase == AudioCallPhase.Connecting && call.Connected && call.PeerJoined)
        {
            call.Timeout?.Dispose();
            call.Timeout = null;
            call.Status = call.Status with { Phase = AudioCallPhase.Active, ActiveSince = clock.GetUtcNow() };
        }
        Changed?.Invoke();
    });

    private void SetTimeout(Session session, Call call, TimeSpan time, string outcome)
    {
        call.Timeout?.Dispose();
        var phase = call.Status.Phase;
        call.Timeout = clock.CreateTimer(_ => dispatcher.Post(() =>
        {
            if (IsCurrent(session, call) && call.Status.Phase == phase)
                _ = CloseAsync(session, call, call.Status.Phase == AudioCallPhase.Incoming, outcome);
        }), null, time, Timeout.InfiniteTimeSpan);
    }

    private void HandleFailure(Session session, Call call, Exception ex)
    {
        var outcome = ex switch
        {
            UserUnavailableException => "Messenger_UserUnavailable",
            RpcException { StatusCode: StatusCode.FailedPrecondition or StatusCode.ResourceExhausted }
                when call.Status.Phase == AudioCallPhase.Outgoing => "Calls_Busy",
            RpcException { StatusCode: StatusCode.NotFound or StatusCode.FailedPrecondition } => "Calls_Ended",
            RpcException { StatusCode: StatusCode.Unimplemented } => "Calls_Unavailable",
            RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded } => "Calls_Network",
            _ => "Calls_Failed"
        };
        if (outcome == "Calls_Unavailable")
        {
            session.Unsupported = true;
            session.Stream?.Cancel();
            Available = false;
        }
        Clear(session, call, outcome);
    }

    private async Task CloseAsync(Session session, Call call, bool reject, string? outcome = null, bool stopping = false)
    {
        var id = call.Status.Id;
        Clear(session, call, outcome);
        if (id != Guid.Empty)
            await FinishAsync(session, id, reject, stopping);
    }

    private async Task FinishAsync(Session session, Guid id, bool reject, bool stopping = false)
    {
        try
        {
            if (!session.Signaling.IsCurrent)
                return;

            using var deadline = stopping ? new CancellationTokenSource(TimeSpan.FromSeconds(3), clock) : null;
            var ct = deadline?.Token ?? session.Lifetime.Token;
            await session.Signaling.FinishAsync(id.ToString("D"), reject, ct).WaitAsync(ct);
        }
        catch (Exception)
        {
            // При потере связи сервер завершает звонок по таймауту или выходу из комнаты.
        }
    }

    private void Clear(Session session, Call call, string? outcome)
    {
        _call = null;
        call.Timeout?.Dispose();
        call.Offer = null;
        if (call.Status.Id != Guid.Empty)
            RememberClosed(session, call.Status.Id);
        media.Disconnect();
        Outcome = outcome;
        Changed?.Invoke();
    }

    private bool IsClosed(Session session, Guid id) =>
        session.Closed.TryGetValue(id, out var expiry) && expiry > clock.GetUtcNow();

    private void RememberClosed(Session session, Guid id)
    {
        var now = clock.GetUtcNow();
        foreach (var expired in session.Closed.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            session.Closed.Remove(expired);
        if (session.Closed.Count >= 256)
            session.Closed.Remove(session.Closed.First().Key);
        session.Closed[id] = now.AddMinutes(1);
    }
}
