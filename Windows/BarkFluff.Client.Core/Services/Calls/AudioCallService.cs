using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Proto.Calls;
using BarkFluff.Shared.Exceptions.Users;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services.Calls;

public enum AudioCallPhase { Outgoing, Incoming, Connecting, Active }

public sealed record AudioCallStatus(Guid Id, long PeerId, string? PeerName, AudioCallPhase Phase,
    DateTimeOffset? ActiveSince = null, bool Muted = false, IReadOnlyList<CallAudioDevice>? Devices = null,
    Guid? ChatId = null, CallMediaType MediaType = CallMediaType.CallMediaAudio,
    CallAudioQuality AudioQuality = CallAudioQuality.Auto, int VideoQuality = 0,
    bool CameraOn = false, bool SharingScreen = false, bool VideoVisible = false, IReadOnlyList<long>? Participants = null);

public sealed partial class AudioCallService(ICallMedia media, IUiDispatcher dispatcher, TimeProvider clock)
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
        public bool AudioQualityUnsupported;
    }

    private sealed class Call(AudioCallStatus status)
    {
        public AudioCallStatus Status = status;
        public readonly bool IsIncoming = status.Phase == AudioCallPhase.Incoming;
        public InitiateCallResponse? Offer;
        public ITimer? Timeout;
        public bool Connected;
        public bool PeerJoined;
        public int AudioQualityRevision;
        public bool AudioQualityPending;
        public CallAudioQuality MediaAudioQuality;
        public readonly HashSet<long> Participants = [];
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
        Available = false;
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
        ResetGroupLookup();
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
                RefreshGroupCall();
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

    public async Task InitiateAsync(long peerId, string peerName,
        CallMediaType mediaType = CallMediaType.CallMediaAudio, Guid? chatId = null)
    {
        if (_session is not { } session || !IsCurrent(session) || !Available || _call is not null ||
            (chatId is null ? peerId <= 0 : chatId == Guid.Empty) ||
            mediaType is not (CallMediaType.CallMediaAudio or CallMediaType.CallMediaVideo))
            return;

        var call = _call = new Call(new(Guid.Empty, peerId, peerName, AudioCallPhase.Outgoing, ChatId: chatId, MediaType: mediaType));
        Outcome = null;
        Changed?.Invoke();
        try
        {
            var request = new InitiateCallRequest { MediaType = mediaType };
            if (chatId is { } group)
                request.ChatId = group.ToString("D");
            else
                request.CalleeUserId = peerId;
            var offer = await session.Signaling.InitiateAsync(request, session.Lifetime.Token);
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
            if (chatId is not null && IsCurrent(session, call) && call.Status.Phase == AudioCallPhase.Outgoing)
            {
                BeginConnecting(session, call);
                await ConnectAsync(session, call, offer.LivekitUrl, offer.AccessToken, offer.AudioQuality);
            }
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
        var qualityRevision = call.AudioQualityRevision;
        BeginConnecting(session, call);
        try
        {
            var answer = await session.Signaling.AcceptAsync(id.ToString("D"), session.Lifetime.Token);
            if (IsCurrent(session, call))
                await ConnectAsync(session, call, answer?.LivekitUrl, answer?.AccessToken,
                    qualityRevision == call.AudioQualityRevision ? answer?.AudioQuality : call.Status.AudioQuality);
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
            Guid? chatId = null;
            if (incoming.ChatId.Length != 0)
            {
                if (!Guid.TryParse(incoming.ChatId, out var group) || group == Guid.Empty)
                    return;
                chatId = group;
            }
            if (incoming.MediaType is not (CallMediaType.CallMediaAudio or CallMediaType.CallMediaVideo) ||
                incoming.CallerUserId <= 0 || !Guid.TryParse(incoming.CallId, out var incomingId) ||
                incomingId == Guid.Empty)
                return;

            ObserveGroupRing(chatId, incomingId);
            if (IsClosed(session, incomingId) || _call?.Status.Id == incomingId)
                return;
            if (_call is not null)
            {
                RememberClosed(session, incomingId);
                _ = FinishAsync(session, incomingId, reject: true);
                return;
            }

            var incomingCall = _call = new Call(new(incomingId, incoming.CallerUserId, null, AudioCallPhase.Incoming,
                ChatId: chatId, MediaType: incoming.MediaType));
            Outcome = null;
            SetTimeout(session, incomingCall, remaining <= TimeSpan.Zero || remaining > RingTimeout
                ? RingTimeout : remaining, "Calls_Missed");
            Changed?.Invoke();
            return;
        }

        var rawId = item.Accepted?.CallId ?? item.Rejected?.CallId ?? item.Ended?.CallId ??
            item.Member?.CallId ?? item.AudioQuality?.CallId;
        if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty)
            return;
        ObserveGroupEvent(id, item);
        if (IsClosed(session, id))
            return;

        if (item.Member is not null || item.AudioQuality is not null)
        {
            if (_call is { } current && id == current.Status.Id)
                ReceiveCallDetails(current, item);
            return;
        }
        if (_call is not { } call || id != call.Status.Id)
        {
            if (_call?.Status.Id == Guid.Empty && (session.Early.ContainsKey(id) || session.Early.Count < 64) &&
                (!session.Early.TryGetValue(id, out var old) || old.Accepted is not null))
                session.Early[id] = item;
            if (_call?.Status.Id != Guid.Empty && (item.Ended is not null || item.Rejected is not null ||
                item.Accepted?.AcceptedByUserId == session.Signaling.UserId))
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
        else if (item.Accepted is not null && call.IsIncoming &&
                 (call.Status.ChatId is null || session.Signaling.UserId == item.Accepted.AcceptedByUserId))
            Clear(session, call, "Calls_AnsweredElsewhere");
        else if (item.Accepted is not null && call.Status.Phase == AudioCallPhase.Outgoing && call.Offer is { } offer)
        {
            BeginConnecting(session, call);
            _ = ConnectAsync(session, call, offer.LivekitUrl, offer.AccessToken,
                call.AudioQualityRevision == 0 ? offer.AudioQuality : call.Status.AudioQuality);
        }
    }

    private void BeginConnecting(Session session, Call call)
    {
        call.Offer = null;
        call.Status = call.Status with { Phase = AudioCallPhase.Connecting };
        SetTimeout(session, call, TimeSpan.FromSeconds(25), "Calls_MediaError");
        Changed?.Invoke();
    }

    private async Task ConnectAsync(Session session, Call call, string? endpoint, string? token, CallAudioQuality? quality)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || !CallMediaBridge.TryGetEndpoint(endpoint ?? string.Empty, out var url))
                throw new InvalidOperationException();
            if (quality is { } received && Enum.IsDefined(received))
                call.Status = call.Status with { AudioQuality = received };
            call.MediaAudioQuality = call.Status.AudioQuality;
            await media.ConnectAsync(call.Status.Id, url, token, session.Lifetime.Token,
                call.Status.MediaType == CallMediaType.CallMediaVideo, call.Status.AudioQuality);
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
            case "participant_left" when call.Status.ChatId is null:
                _ = CloseAsync(session, call, false, "Calls_Ended");
                return;
            case "connected":
                call.Connected = true;
                SyncAudioQuality(call);
                break;
            case "participant_joined":
                call.PeerJoined = true;
                break;
            case "participants":
                call.Participants.Clear();
                call.Participants.UnionWith(message.Participants!);
                UpdateParticipants(call);
                break;
            case "camera_on":
            case "camera_off":
                call.Status = call.Status with { CameraOn = message.Type == "camera_on" };
                break;
            case "screen_on":
            case "screen_off":
                call.Status = call.Status with { SharingScreen = message.Type == "screen_on" };
                break;
            case "video_visible":
            case "video_hidden":
                call.Status = call.Status with { VideoVisible = message.Type == "video_visible" };
                break;
            case "camera_error":
            case "screen_error":
            case "quality_error":
                Outcome = message.Type switch
                {
                    "camera_error" => "Calls_CameraError",
                    "screen_error" => "Calls_ScreenError",
                    _ => "Calls_QualityError"
                };
                break;
            case "devices":
                call.Status = call.Status with { Devices = message.Devices };
                break;
        }

        if (_call != call)
            return;
        if (call.Status.Phase == AudioCallPhase.Connecting && call.Connected && (call.PeerJoined || call.Status.ChatId is not null))
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

    private void HandleFailure(Session session, Call call, Exception ex, bool disableUnavailable = true)
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
        if (outcome == "Calls_Unavailable" && disableUnavailable)
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
        if (id != Guid.Empty && (reject || call.Status.ChatId is null || call.Participants.Count == 0))
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
        RefreshGroupCall();
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
