using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Calls;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Time.Testing;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AudioCallTests
{
    [Fact]
    public async Task OutgoingHandlesAcceptanceBeforeRpcReturnsAndFinishesOnRemoteEnd()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        var response = new TaskCompletionSource<InitiateCallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Offer = response.Task;
        await test.Emit(new());
        var dial = test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        Assert.Equal(AudioCallPhase.Outgoing, test.Calls.Current?.Phase);
        await test.Emit(Accepted(id));
        response.SetResult(new() { CallId = Format(id), LivekitUrl = Endpoint, AccessToken = "jwt" });
        await dial.WaitAsync(TestTimeout);
        Assert.Equal(AudioCallPhase.Connecting, test.Calls.Current?.Phase);
        Assert.Equal(id, test.Media.ConnectedTo);
        test.Media.Emit(new(Guid.NewGuid(), "error"));
        test.Media.Emit(new(id, "participant_joined"));
        Assert.Equal(AudioCallPhase.Connecting, test.Calls.Current?.Phase);
        test.Media.Emit(new(id, "connected"));
        Assert.Equal(AudioCallPhase.Active, test.Calls.Current?.Phase);
        await test.Emit(Ended(Guid.NewGuid(), CallEndReason.CallEndHangup));
        Assert.Equal(AudioCallPhase.Active, test.Calls.Current?.Phase);
        await test.Emit(Ended(id, CallEndReason.CallEndHangup));
        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Equal("Calls_Ended", test.Calls.Outcome);
        await test.Emit(Incoming(id));
        Assert.Null(test.Calls.Current);
    }

    [Fact]
    public async Task IncomingAcceptsThenMutesSelectsDeviceAndHangsUp()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        await test.Emit(Incoming(id));
        await test.Emit(Incoming(id));
        Assert.Equal(AudioCallPhase.Incoming, test.Calls.Current?.Phase);
        await test.Ui.RunAsync(test.Calls.AcceptAsync);
        Assert.Equal(id, test.Signal.AcceptedId);
        test.Media.Emit(new(id, "connected"));
        test.Media.Emit(new(id, "participant_joined"));
        var microphone = new CallAudioDevice("mic", "Microphone", "audioinput");
        test.Media.Emit(new(id, "devices", [microphone]));
        test.Ui.Post(() =>
        {
            test.Calls.ToggleMute();
            test.Calls.SelectDevice(microphone);
            test.Calls.ToggleMute();
        });
        Assert.Equal([(id, "mute", null), (id, "audioinput", "mic"), (id, "unmute", null)], test.Media.Commands);
        await test.Ui.RunAsync(test.Calls.HangUpAsync);
        Assert.Equal([(id, false)], test.Signal.Finished);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Null(test.Calls.Current);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("accepted")]
    [InlineData("rejected")]
    public async Task IncomingRingDismissesOnAnswerOrRejection(string action)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        if (action == "accepted")
        {
            await test.Emit(Accepted(id));
            await test.Emit(Incoming(id));
            Assert.Null(test.Calls.Current);
            id = Guid.NewGuid();
        }
        await test.Emit(Incoming(id));
        if (action == "local")
            await test.Ui.RunAsync(test.Calls.HangUpAsync);
        else if (action == "accepted")
            await test.Emit(Accepted(id));
        else
            await test.Emit(new() { Rejected = new() { CallId = Format(id) } });
        Assert.Null(test.Calls.Current);
        Assert.Equal(action switch { "accepted" => "Calls_AnsweredElsewhere", "rejected" => "Calls_Rejected", _ => null },
            test.Calls.Outcome);
        Assert.Equal(action == "local" ? [(id, true)] : Array.Empty<(Guid, bool)>(), test.Signal.Finished);
        await test.Emit(Incoming(id));
        Assert.Null(test.Calls.Current);
    }

    [Fact]
    public async Task AnswerOnAnotherDeviceWinsWhileLocalAcceptIsPending()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        var answer = new TaskCompletionSource<AcceptCallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Answer = answer.Task;

        await test.Emit(Incoming(id));
        var accepting = test.Ui.RunAsync(test.Calls.AcceptAsync);
        await test.Emit(Accepted(id));
        answer.SetResult(new() { LivekitUrl = Endpoint, AccessToken = "jwt" });
        await accepting.WaitAsync(TestTimeout);

        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Equal("Calls_AnsweredElsewhere", test.Calls.Outcome);
        Assert.Empty(test.Signal.Finished);
    }

    [Fact]
    public async Task SecondIncomingIsRejectedOnceAndOriginalRingTimesOut()
    {
        await using var test = new Fixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await test.Emit(Incoming(first));
        await test.Emit(Incoming(second));
        await test.Emit(Incoming(second));
        Assert.Equal(first, test.Calls.Current?.Id);
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(50)));
        Assert.Equal([(second, true), (first, true)], test.Signal.Finished);
        Assert.Equal("Calls_Missed", test.Calls.Outcome);
        Assert.Null(test.Calls.Current);
    }

    [Fact]
    public async Task ServerRejectionAndBusyEndOutgoingCall()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        await test.Emit(new());
        test.Signal.Offer = Task.FromException<InitiateCallResponse>(new RpcException(new Status(StatusCode.FailedPrecondition, "")));
        await test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        Assert.Equal("Calls_Busy", test.Calls.Outcome);
        test.Signal.Offer = Task.FromResult(new InitiateCallResponse { CallId = Format(id), LivekitUrl = Endpoint, AccessToken = "jwt" });
        await test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        await test.Emit(new() { Rejected = new() { CallId = Format(id) } });
        Assert.Equal("Calls_Rejected", test.Calls.Outcome);
        Assert.Null(test.Media.ConnectedTo);
    }

    [Theory]
    [InlineData("disconnected", "Calls_MediaError")]
    [InlineData("error", "Calls_MediaError")]
    [InlineData("runtime", "Calls_Runtime")]
    [InlineData("reset", null)]
    public async Task DisconnectsMediaOnEveryLocalExit(string exit, string? outcome)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        if (exit == "runtime")
            test.Media.ConnectFailure = new CallRuntimeException();
        await test.Emit(Incoming(id));
        await test.Ui.RunAsync(test.Calls.AcceptAsync);
        switch (exit)
        {
            case "disconnected":
            case "error":
                test.Media.Emit(new(id, exit));
                break;
            case "reset":
                test.Ui.Post(test.Calls.Stop);
                test.Media.Emit(new(id, "connected"));
                break;
        }
        Assert.Equal(outcome, test.Calls.Outcome);
        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Equal([(id, false)], test.Signal.Finished);
    }

    [Theory]
    [InlineData(AudioCallPhase.Incoming)]
    [InlineData(AudioCallPhase.Outgoing)]
    [InlineData(AudioCallPhase.Connecting)]
    [InlineData(AudioCallPhase.Active)]
    public async Task SignalingOutageKeepsCallDuringReconnect(AudioCallPhase phase)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        if (phase == AudioCallPhase.Outgoing)
        {
            await test.Emit(new());
            test.Signal.Offer = Task.FromResult(new InitiateCallResponse { CallId = Format(id) });
            await test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        }
        else
        {
            await test.Emit(Incoming(id));
            if (phase is AudioCallPhase.Connecting or AudioCallPhase.Active)
                await test.Ui.RunAsync(test.Calls.AcceptAsync);
            if (phase == AudioCallPhase.Active)
            {
                test.Media.Emit(new(id, "connected"));
                test.Media.Emit(new(id, "participant_joined"));
            }
        }

        test.Ui.Post(() => test.Signal.CloseStream(StatusCode.Unavailable, reopen: true));
        await test.WaitForAsync(() => !test.Calls.Available);
        Assert.Equal(phase, test.Calls.Current?.Phase);
        await test.WaitForAsync(() => test.Calls.Available);
        Assert.Equal(id, test.Calls.Current?.Id);
        Assert.Equal(phase, test.Calls.Current?.Phase);
        Assert.Empty(test.Signal.Finished);
        if (phase is AudioCallPhase.Connecting or AudioCallPhase.Active)
            Assert.Equal(id, test.Media.ConnectedTo);
        await test.Emit(Ended(id, CallEndReason.CallEndHangup));
        Assert.Null(test.Calls.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenewsSilentOrAgedSignalingStream(bool receivingEvents)
    {
        await using var test = new Fixture();
        await test.Emit(new());
        if (receivingEvents)
        {
            for (var i = 0; i < 2; i++)
            {
                test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(60)));
                await test.Emit(new());
                Assert.Equal(1, test.Signal.Subscriptions);
            }
        }
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(receivingEvents ? 59 : 89)));
        Assert.Equal(1, test.Signal.Subscriptions);
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, test.Signal.Subscriptions);
        await test.Emit(new());
        Assert.True(test.Calls.Available);
    }

    [Fact]
    public async Task IncomingWithClockSkewRingsForFullLocalTimeout()
    {
        await using var test = new Fixture();
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromMinutes(5)));
        var id = Guid.NewGuid();
        await test.Emit(Incoming(id));
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(49)));
        Assert.Equal(id, test.Calls.Current?.Id);
        test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(1)));
        Assert.Null(test.Calls.Current);
        Assert.Equal("Calls_Missed", test.Calls.Outcome);
        Assert.Equal([(id, true)], test.Signal.Finished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopForLogoutFinishesCallWhileSignalingStreamIsDown(bool timeout)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        await test.Emit(Incoming(id));
        await test.Ui.RunAsync(test.Calls.AcceptAsync);
        test.Signal.CloseStream(StatusCode.Unavailable);
        await test.WaitForAsync(() => !test.Calls.Available);
        var server = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Finish = server.Task;

        var stopping = test.Ui.RunAsync(test.Calls.StopAsync);
        Assert.Equal([(id, false)], test.Signal.Finished);
        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
        Assert.False(stopping.IsCompleted);

        if (timeout)
            test.Ui.Post(() => test.Clock.Advance(TimeSpan.FromSeconds(3)));
        else
            server.SetResult();
        await stopping.WaitAsync(TestTimeout);
        Assert.Equal(timeout, test.Signal.FinishToken.IsCancellationRequested);
    }

    [Fact]
    public async Task LateOutgoingResponseAfterHangUpEndsOnlyItsOwnCall()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        var incoming = Guid.NewGuid();
        var response = new TaskCompletionSource<InitiateCallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Offer = response.Task;
        await test.Emit(new());
        var dial = test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        await test.Ui.RunAsync(test.Calls.HangUpAsync);
        await test.Emit(Incoming(incoming));
        response.SetResult(new() { CallId = Format(id), LivekitUrl = Endpoint, AccessToken = "jwt" });
        await dial.WaitAsync(TestTimeout);
        Assert.Equal([(id, false)], test.Signal.Finished);
        Assert.Equal(incoming, test.Calls.Current?.Id);
    }

    [Fact]
    public async Task OutgoingReportsUnavailablePeerWithoutOpeningMedia()
    {
        await using var test = new Fixture();
        await test.Emit(new());
        test.Signal.Offer = Task.FromException<InitiateCallResponse>(
            new BarkFluff.Shared.Exceptions.Users.UserUnavailableException());
        await test.Ui.RunAsync(() => test.Calls.InitiateAsync(20, "Peer"));
        Assert.Equal("Messenger_UserUnavailable", test.Calls.Outcome);
        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Empty(test.Signal.Finished);
    }

    [Fact]
    public async Task UnsupportedStreamStopsForTheSessionEvenAfterTokenRefresh()
    {
        await using var test = new Fixture();
        test.Signal.CloseStream(StatusCode.Unimplemented);
        await test.WaitForAsync(() => test.Calls.Outcome == "Calls_Unavailable");
        test.Signal.RefreshToken();
        Assert.False(test.Calls.Available);
        Assert.Equal(1, test.Signal.Subscriptions);
    }

    [Theory]
    [InlineData("{\"type\":\"unknown\",\"call_id\":\"{id}\"}")]
    [InlineData("{\"type\":\"connected\",\"call_id\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"type\":\"connected\",\"call_id\":\"{id}\",\"token\":\"extra\"}")]
    [InlineData("{\"type\":\"connected\",\"call_id\":42}")]
    [InlineData("{\"type\":\"devices\",\"call_id\":\"{id}\",\"devices\":[{\"id\":\"id\",\"kind\":\"videoinput\",\"label\":\"name\"}]}")]
    [InlineData("{\"type\":\"devices\",\"call_id\":\"{id}\",\"devices\":null}")]
    [InlineData("not json")]
    public void BridgeDiscardsInvalidMessages(string payload)
    {
        var id = Guid.NewGuid();
        Assert.False(CallMediaBridge.TryRead(payload.Replace("{id}", Format(id)), id, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void BridgeAcceptsValidEventsAndAllowsOnlyCredentialFreeSecureEndpoints()
    {
        var id = Guid.NewGuid();
        Assert.True(CallMediaBridge.TryRead($$"""{"type":"devices","call_id":"{{id:D}}","devices":[{"id":"default","kind":"audioinput","label":"Microphone"}]}""", id, out var message));
        Assert.Equal("default", Assert.Single(message!.Devices!).Id);
        Assert.True(CallMediaBridge.TryGetEndpoint(Endpoint, out _));
        Assert.False(CallMediaBridge.TryGetEndpoint("ws://calls.example.test", out _));
        Assert.False(CallMediaBridge.TryGetEndpoint("wss://calls.example.test/?token=jwt", out _));
        Assert.False(CallMediaBridge.TryGetEndpoint("wss://user@calls.example.test", out _));
    }

    private const string Endpoint = "wss://calls.example.test";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static string Format(Guid id) => id.ToString("D");
    private static CallEvent Incoming(Guid id) => new()
    {
        Incoming = new() { CallId = Format(id), CallerUserId = 20, MediaType = CallMediaType.CallMediaAudio,
            StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime) }
    };
    private static CallEvent Accepted(Guid id) => new() { Accepted = new() { CallId = Format(id) } };
    private static CallEvent Ended(Guid id, CallEndReason reason) => new() { Ended = new() { CallId = Format(id), Reason = reason } };

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset StartTime = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public readonly InlineUiDispatcher Ui = new(serializeContinuations: true);
        public readonly FakeTimeProvider Clock = new(StartTime);
        public readonly Media Media = new();
        public readonly Signaling Signal = new();
        public readonly AudioCallService Calls;

        public Fixture()
        {
            Calls = new(Media, Ui, Clock);
            Ui.Post(() => Calls.Start(Signal));
        }

        public async Task Emit(CallEvent item)
        {
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Signal.Events.Writer.WriteAsync((item, seen));
            await seen.Task.WaitAsync(TestTimeout);
        }

        public async Task WaitForAsync(Func<bool> predicate)
        {
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed()
            {
                if (predicate())
                    seen.TrySetResult();
            }
            Ui.Post(() =>
            {
                Calls.Changed += Changed;
                Changed();
            });
            try
            {
                await seen.Task.WaitAsync(TestTimeout);
                Ui.Post(() => { });
            }
            finally
            {
                Ui.Post(() => Calls.Changed -= Changed);
            }
        }

        public ValueTask DisposeAsync()
        {
            Ui.Post(Calls.Stop);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Signaling : ICallSignaling
    {
        public Channel<(CallEvent Item, TaskCompletionSource Seen)> Events { get; private set; } =
            System.Threading.Channels.Channel.CreateUnbounded<(CallEvent, TaskCompletionSource)>();
        public readonly List<(Guid, bool)> Finished = [];
        public Task<InitiateCallResponse> Offer { get; set; } = Task.FromResult(new InitiateCallResponse());
        public Task<AcceptCallResponse> Answer { get; set; } =
            Task.FromResult(new AcceptCallResponse { LivekitUrl = Endpoint, AccessToken = "jwt" });
        public Task Finish { get; set; } = Task.CompletedTask;
        public CancellationToken FinishToken { get; private set; }
        public Guid? AcceptedId { get; private set; }
        public int Subscriptions { get; private set; }
        public bool IsCurrent => true;
        public bool IsAdvertised => true;
        public event EventHandler? TokenRefreshed;
        public void RefreshToken() => TokenRefreshed?.Invoke(this, EventArgs.Empty);

        public Task<InitiateCallResponse> InitiateAsync(long userId, CancellationToken ct) => Offer;
        public Task<AcceptCallResponse> AcceptAsync(string id, CancellationToken ct)
        {
            AcceptedId = Guid.Parse(id);
            return Answer;
        }
        public Task FinishAsync(string id, bool reject, CancellationToken ct)
        {
            Finished.Add((Guid.Parse(id), reject));
            FinishToken = ct;
            return Finish;
        }
        public Task<IAsyncEnumerable<CallEvent>?> SubscribeAsync(CancellationToken ct)
        {
            Subscriptions++;
            return Task.FromResult<IAsyncEnumerable<CallEvent>?>(ReadAsync(ct));
        }

        private StatusCode _failure;

        public void CloseStream(StatusCode failure, bool reopen = false)
        {
            _failure = failure;
            Events.Writer.Complete();
            if (reopen)
                Events = System.Threading.Channels.Channel.CreateUnbounded<(CallEvent, TaskCompletionSource)>();
        }

        private async IAsyncEnumerable<CallEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var (item, seen) in Events.Reader.ReadAllAsync(ct))
            {
                yield return item;
                seen.TrySetResult();
            }
            if (_failure != StatusCode.OK)
                throw new RpcException(new Status(_failure, string.Empty));
        }
    }

    private sealed class Media : ICallMedia
    {
        public event Action<CallMediaEvent>? Received;
        public Guid? ConnectedTo { get; private set; }
        public Exception? ConnectFailure { get; set; }
        public readonly List<(Guid, string, string?)> Commands = [];
        public void Emit(CallMediaEvent message) => Received?.Invoke(message);
        public Task ConnectAsync(Guid callId, Uri url, string token, CancellationToken ct)
        {
            ConnectedTo = callId;
            return ConnectFailure is { } error ? Task.FromException(error) : Task.CompletedTask;
        }
        public void Send(Guid callId, string type, string? deviceId) => Commands.Add((callId, type, deviceId));
        public void Disconnect() => ConnectedTo = null;
    }
}
