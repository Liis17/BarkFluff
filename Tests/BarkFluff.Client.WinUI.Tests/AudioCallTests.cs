using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Calls;
using BarkFluff.Client.Core.ViewModels;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateOutgoingResponseAfterHangUpEndsOnlyItsOwnCall(bool group)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        var incoming = Guid.NewGuid();
        var response = new TaskCompletionSource<InitiateCallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Offer = response.Task;
        await test.Emit(new());
        var dial = test.Ui.RunAsync(() => group
            ? test.Calls.InitiateAsync(0, "Group", CallMediaType.CallMediaAudio, Guid.NewGuid())
            : test.Calls.InitiateAsync(20, "Peer"));
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
    [InlineData("{\"type\":\"devices\",\"call_id\":\"{id}\",\"devices\":[{\"id\":\"id\",\"kind\":\"screeninput\",\"label\":\"name\"}]}")]
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
    [Fact]
    public async Task GroupVideoStartsJoinsAndLeavesWithoutEndingForOthers()
    {
        await using var test = new Fixture();
        await test.WaitForAsync(() => test.Calls.Available);
        var chat = Guid.NewGuid();
        var id = Guid.NewGuid();
        test.Signal.Offer = Task.FromResult(new InitiateCallResponse
        {
            CallId = Format(id), LivekitUrl = Endpoint, AccessToken = "jwt", AudioQuality = CallAudioQuality.Medium
        });

        await test.Ui.RunAsync(() => test.Calls.InitiateAsync(0, "Group", CallMediaType.CallMediaVideo, chat));
        Assert.Equal(Format(chat), test.Signal.OutgoingRequest?.ChatId);
        Assert.Equal(CallMediaType.CallMediaVideo, test.Signal.OutgoingRequest?.MediaType);
        Assert.True(test.Media.Video);
        Assert.Equal(CallAudioQuality.Medium, test.Media.InitialQuality);
        test.Media.Emit(new(id, "connected"));
        Assert.Equal(AudioCallPhase.Active, test.Calls.Current?.Phase);
        test.Media.Emit(new(id, "participants", Participants: [20]));
        await test.Ui.RunAsync(test.Calls.HangUpAsync);
        Assert.Empty(test.Signal.Finished);
        Assert.Null(test.Media.ConnectedTo);

        test.Signal.Active = _ => Task.FromResult(new GetActiveCallsResponse
        {
            Calls = { new ActiveCallItem { CallId = Format(id), ChatId = Format(chat), MediaType = CallMediaType.CallMediaVideo } }
        });
        test.Ui.Post(() => test.Calls.WatchGroup(chat));
        Assert.Equal(id, test.Calls.JoinableCall?.Id);
        await test.Ui.RunAsync(() => test.Calls.JoinAsync("Group"));
        Assert.Equal(id, test.Signal.JoinedId);
        Assert.True(test.Media.Video);
        test.Media.Emit(new(id, "connected"));
        test.Media.Emit(new(id, "participants", Participants: [20, 30]));
        Assert.Equal([20L, 30L], test.Calls.Current?.Participants);
        test.Media.Emit(new(id, "participant_left"));
        await test.Emit(new() { Member = new() { CallId = Format(id), UserId = 30, Action = ParticipantAction.ParticipantLeft } });
        await test.Emit(new() { Member = new() { CallId = Format(id), UserId = 40, Action = ParticipantAction.ParticipantJoined } });
        Assert.Equal([20L, 40L], test.Calls.Current?.Participants);
        Assert.Equal(AudioCallPhase.Active, test.Calls.Current?.Phase);
        await test.Ui.RunAsync(test.Calls.HangUpAsync);
        Assert.Null(test.Media.ConnectedTo);
        Assert.Empty(test.Signal.Finished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncomingVideoUsesCameraAndDisconnectsWhenEnded(bool group)
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        var incoming = Incoming(id);
        incoming.Incoming.MediaType = CallMediaType.CallMediaVideo;
        if (group)
            incoming.Incoming.ChatId = Format(Guid.NewGuid());
        await test.Emit(incoming);
        Assert.Equal(AudioCallPhase.Incoming, test.Calls.Current?.Phase);
        if (group)
        {
            await test.Emit(new() { Accepted = new() { CallId = Format(id), AcceptedByUserId = 30 } });
            Assert.Equal(AudioCallPhase.Incoming, test.Calls.Current?.Phase);
        }
        await test.Ui.RunAsync(test.Calls.AcceptAsync);
        Assert.True(test.Media.Video);
        test.Media.Emit(new(id, "connected"));
        test.Media.Emit(new(id, "participant_joined"));
        test.Media.Emit(new(id, "camera_on"));
        test.Media.Emit(new(id, "screen_on"));
        Assert.True(test.Calls.Current?.CameraOn);
        Assert.True(test.Calls.Current?.SharingScreen);
        await test.Emit(Ended(id, CallEndReason.CallEndHangup));
        Assert.Null(test.Media.ConnectedTo);
        Assert.Null(test.Calls.Current);
    }

    [Fact]
    public async Task AudioQualityEventWinsOverPendingCommandAndVideoQualityIsLocal()
    {
        await using var test = new Fixture();
        var id = Guid.NewGuid();
        await test.Emit(Incoming(id));
        await test.Ui.RunAsync(test.Calls.AcceptAsync);
        test.Media.Emit(new(id, "connected"));
        test.Media.Emit(new(id, "participant_joined"));

        var reply = new TaskCompletionSource<SetCallAudioQualityResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.QualityReply = reply.Task;
        var update = test.Ui.RunAsync(() => test.Calls.SetAudioQualityAsync(CallAudioQuality.High));
        Assert.Equal([(Format(id), CallAudioQuality.High)], test.Signal.QualityRequests);
        await test.Emit(new() { AudioQuality = new() { CallId = Format(id), Quality = CallAudioQuality.Low } });
        reply.SetResult(new());
        await update;
        Assert.Equal(CallAudioQuality.Low, test.Calls.Current?.AudioQuality);
        test.Ui.Post(() => test.Calls.SetVideoQuality(3));
        Assert.Equal(3, test.Calls.Current?.VideoQuality);
        Assert.Equal([(id, "audio_quality", "1"), (id, "video_quality", "3")], test.Media.Commands);
        Assert.Single(test.Signal.QualityRequests);
    }

    [Fact]
    public async Task GroupLookupRejectsOldChatResponseAndStopsOnUnimplemented()
    {
        await using var test = new Fixture();
        await test.WaitForAsync(() => test.Calls.Available);
        var old = Guid.NewGuid();
        var next = Guid.NewGuid();
        var late = new TaskCompletionSource<GetActiveCallsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.Active = chat => chat == Format(old) ? late.Task :
            Task.FromException<GetActiveCallsResponse>(new RpcException(new Status(StatusCode.Unimplemented, "")));
        test.Ui.Post(() => test.Calls.WatchGroup(old));
        test.Ui.Post(() => test.Calls.WatchGroup(next));
        late.SetResult(new GetActiveCallsResponse
        {
            Calls = { new ActiveCallItem { ChatId = Format(old), CallId = Format(Guid.NewGuid()), MediaType = CallMediaType.CallMediaVideo } }
        });
        test.Ui.Post(() => test.Calls.WatchGroup(old));
        Assert.Null(test.Calls.JoinableCall);
        Assert.Equal([Format(old), Format(next)], test.Signal.ActiveRequests);
        await test.Emit(Ended(Guid.NewGuid(), CallEndReason.CallEndHangup));
        Assert.True(test.Calls.Available);
    }

    [Fact]
    public async Task HistoryFiltersAndPagesWithoutDuplicates()
    {
        await using var test = new Fixture();
        using var history = new CallHistoryViewModel(test.Signal, (item, id) => new(id, item, "", ""));
        var newer = new CallHistoryItem { CallId = Format(Guid.NewGuid()), StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime) };
        var older = new CallHistoryItem { CallId = Format(Guid.NewGuid()), StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime.AddMinutes(-5)) };
        var requests = new List<ListCallHistoryRequest>();
        test.Signal.History = request =>
        {
            requests.Add(request);
            return Task.FromResult(request.Filter == CallHistoryFilter.CallHistoryMissed
                ? new ListCallHistoryResponse { Items = { older } }
                : request.BeforeStartedAt is null
                    ? new ListCallHistoryResponse { Items = { newer }, HasMore = true }
                    : new ListCallHistoryResponse { Items = { newer, older } });
        };
        await test.Ui.RunAsync(history.LoadMoreAsync);
        await test.Ui.RunAsync(history.LoadMoreAsync);
        Assert.Equal(newer.StartedAt, requests[1].BeforeStartedAt);
        Assert.Equal([newer.CallId, older.CallId], history.Items.Select(entry => entry.Call.CallId));
        Assert.False(history.HasMore);
        await test.Ui.RunAsync(() => history.SetFilterAsync(CallHistoryFilter.CallHistoryMissed));
        Assert.Equal(CallHistoryFilter.CallHistoryMissed, requests[2].Filter);
        Assert.Null(requests[2].BeforeStartedAt);
        Assert.Equal(older, Assert.Single(history.Items).Call);
    }

    [Fact]
    public async Task GroupInitiatorEndsCallBeforeFirstParticipantJoins()
    {
        await using var test = new Fixture();
        await test.WaitForAsync(() => test.Calls.Available);
        var id = Guid.NewGuid();
        test.Signal.Offer = Task.FromResult(new InitiateCallResponse
        {
            CallId = Format(id), LivekitUrl = Endpoint, AccessToken = "jwt"
        });

        await test.Ui.RunAsync(() => test.Calls.InitiateAsync(0, "Group", CallMediaType.CallMediaAudio, Guid.NewGuid()));
        test.Media.Emit(new(id, "connected"));
        await test.Ui.RunAsync(test.Calls.HangUpAsync);

        Assert.Equal((id, false), Assert.Single(test.Signal.Finished));
        Assert.Null(test.Calls.Current);
        Assert.Null(test.Media.ConnectedTo);
    }

    [Fact]
    public async Task HistoryIgnoresOldResponseAfterFilterChanges()
    {
        await using var test = new Fixture();
        var history = new CallHistoryViewModel(test.Signal, (item, id) => new(id, item, "", ""));
        var stale = new CallHistoryItem { CallId = Format(Guid.NewGuid()), StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime) };
        var missed = new CallHistoryItem { CallId = Format(Guid.NewGuid()), StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime.AddMinutes(-1)) };
        var oldReply = new TaskCompletionSource<ListCallHistoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Signal.History = request => request.Filter == CallHistoryFilter.CallHistoryMissed
            ? Task.FromResult(new ListCallHistoryResponse { Items = { missed } })
            : oldReply.Task;

        try
        {
            var oldLoad = test.Ui.RunAsync(history.LoadMoreAsync);
            await test.Ui.RunAsync(() => history.SetFilterAsync(CallHistoryFilter.CallHistoryMissed));

            oldReply.SetResult(new ListCallHistoryResponse { Items = { stale }, HasMore = true });
            await oldLoad.WaitAsync(TestTimeout);
            test.Ui.Post(() =>
            {
                Assert.Equal(missed, Assert.Single(history.Items).Call);
                Assert.False(history.HasMore);
                Assert.False(history.IsLoading);
            });
        }
        finally
        {
            test.Ui.Post(history.Dispose);
        }
    }

    [Fact]
    public void VideoBridgeAcceptsKnownEventsAndRejectsUnexpectedFields()
    {
        var id = Guid.NewGuid();
        Assert.True(CallMediaBridge.TryRead($$"""{"type":"camera_on","call_id":"{{id:D}}"}""", id, out _));
        Assert.True(CallMediaBridge.TryRead($$"""{"type":"screen_off","call_id":"{{id:D}}"}""", id, out _));
        Assert.False(CallMediaBridge.TryRead($$"""{"type":"screen_on","call_id":"{{id:D}}","url":"https://example.test"}""", id, out _));
        Assert.True(CallMediaBridge.TryRead($$"""{"type":"participants","call_id":"{{id:D}}","participants":["9007199254740993"]}""",
            id, out var message));
        Assert.Equal([9007199254740993L], message?.Participants);
        Assert.False(CallMediaBridge.TryRead($$"""{"type":"participants","call_id":"{{id:D}}","participants":["1","1"]}""", id, out _));
        Assert.False(CallMediaBridge.TryRead($$"""{"type":"participants","call_id":"{{id:D}}","participants":["9223372036854775808"]}""", id, out _));
    }

    [Theory]
    [InlineData("https://call.barkfluff.invalid/room.html", true)]
    [InlineData("https://call.barkfluff.invalid/", true)]
    [InlineData("https://call-assets.barkfluff.invalid/room.html", false)]
    [InlineData("https://call.barkfluff.invalid.example/room.html", false)]
    [InlineData("https://call.barkfluff.invalid:8443/room.html", false)]
    [InlineData("https://example@call.barkfluff.invalid/room.html", false)]
    [InlineData("http://call.barkfluff.invalid/room.html", false)]
    public void CallPermissionsRequireTheDocumentOrigin(string source, bool allowed)
    {
        Assert.Equal(allowed, CallMediaBridge.MatchesOrigin(source, "https://call.barkfluff.invalid"));
    }

    private static CallEvent Incoming(Guid id) => new()
    {
        Incoming = new() { CallId = Format(id), CallerUserId = 20, MediaType = CallMediaType.CallMediaAudio,
            StartedAt = Timestamp.FromDateTimeOffset(Fixture.StartTime) }
    };
    private static CallEvent Accepted(Guid id) => new() { Accepted = new() { CallId = Format(id), AcceptedByUserId = 10 } };
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

    internal sealed class Signaling : ICallSignaling
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
        public long UserId => 10;
        public InitiateCallRequest? OutgoingRequest;
        public Guid? JoinedId;
        public List<string> ActiveRequests { get; } = [];
        public Func<string, Task<GetActiveCallsResponse>> Active { get; set; } = _ => Task.FromResult(new GetActiveCallsResponse());
        public List<(string Id, CallAudioQuality Quality)> QualityRequests { get; } = [];
        public Task<SetCallAudioQualityResponse> QualityReply { get; set; } = Task.FromResult(new SetCallAudioQualityResponse());
        public Func<ListCallHistoryRequest, Task<ListCallHistoryResponse>> History { get; set; } =
            _ => Task.FromResult(new ListCallHistoryResponse());
        public bool IsAdvertised => true;
        public event EventHandler? TokenRefreshed;
        public void RefreshToken() => TokenRefreshed?.Invoke(this, EventArgs.Empty);

        public Task<InitiateCallResponse> InitiateAsync(InitiateCallRequest request, CancellationToken ct)
        {
            OutgoingRequest = request;
            return Offer;
        }
        public Task<JoinCallResponse> JoinAsync(string id, CancellationToken ct)
        {
            JoinedId = Guid.Parse(id);
            return Task.FromResult(new JoinCallResponse { LivekitUrl = Endpoint, AccessToken = "jwt" });
        }
        public Task<GetActiveCallsResponse> GetActiveAsync(string chatId, CancellationToken ct)
        {
            ActiveRequests.Add(chatId);
            return Active(chatId);
        }
        public Task<ListCallHistoryResponse> ListHistoryAsync(ListCallHistoryRequest request, CancellationToken ct) =>
            History(request);
        public Task<SetCallAudioQualityResponse> SetAudioQualityAsync(string id, CallAudioQuality quality, CancellationToken ct)
        {
            QualityRequests.Add((id, quality));
            return QualityReply;
        }
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

    internal sealed class Media : ICallMedia
    {
        public event Action<CallMediaEvent>? Received;
        public Guid? ConnectedTo { get; private set; }
        public Exception? ConnectFailure { get; set; }
        public readonly List<(Guid, string, string?)> Commands = [];
        public void Emit(CallMediaEvent message) => Received?.Invoke(message);
        public bool Video { get; private set; }
        public CallAudioQuality InitialQuality { get; private set; }
        public Task ConnectAsync(Guid callId, Uri url, string token, CancellationToken ct, bool video, CallAudioQuality quality)
        {
            ConnectedTo = callId;
            Video = video;
            InitialQuality = quality;
            return ConnectFailure is { } error ? Task.FromException(error) : Task.CompletedTask;
        }
        public void Send(Guid callId, string type, string? deviceId) => Commands.Add((callId, type, deviceId));
        public void Disconnect() => ConnectedTo = null;
    }
}
