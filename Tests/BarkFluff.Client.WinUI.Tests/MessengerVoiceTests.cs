using System.Buffers.Binary;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Calls;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Files.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Microsoft.Extensions.Time.Testing;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerVoiceTests
{
    [Theory]
    [InlineData(24_017, 48_000, 24_017)]
    [InlineData(24_800, 24_017, 24_017)]
    public async Task OggOpusHasVoiceSignatureAndPreservesDuration(int capturedSamples, int limit, int expectedSamples)
    {
        using var pcm = Wave(capturedSamples);
        using var ogg = new MemoryStream();
        Assert.True(VoiceOggCodec.EncodeWave(pcm, ogg, 24_000, limit, default));
        Assert.Equal(DetectedFileType.Voice, await new FileTypeDetector().DetectAsync(ogg));

        byte[] bytes = ogg.ToArray();
        Assert.True(bytes.AsSpan(0, 4).SequenceEqual("OggS"u8));
        Assert.True(bytes.AsSpan(28, 8).SequenceEqual("OpusHead"u8));
        Assert.True(bytes.AsSpan(75, 8).SequenceEqual("OpusTags"u8));
        int preSkip = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(38));
        Assert.True(preSkip > 0);
        int cursor = 0;
        int last = 0;
        while (cursor < bytes.Length)
        {
            last = cursor;
            int segments = bytes[cursor + 26];
            cursor += 27 + segments;
            for (int i = 0; i < segments; i++)
                cursor += bytes[last + 27 + i];
        }
        Assert.Equal(bytes.Length, cursor);
        Assert.Equal(4, bytes[last + 5] & 4);
        Assert.Equal(expectedSamples, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(last + 6)) - preSkip);

        using var decoded = new MemoryStream();
        ogg.Position = 0;
        VoiceOggCodec.DecodeWave(ogg, decoded, default);
        Assert.Equal(44 + expectedSamples * 2, decoded.Length);
        Assert.Contains(decoded.ToArray().Skip(44), value => value != 0);
    }

    [Fact]
    public async Task VoiceSendsWithoutTextWithReplyAndSurvivesUploadAndSendFailures()
    {
        var recorder = new Recorder();
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var (vm, service) = await OpenAsync(recorder, dispatcher: dispatcher);
        vm.StartReplyMessageCommand.Execute(Assert.Single(vm.Messages));
        Assert.True(vm.ShowVoiceMicrophone);
        var recording = vm.RecordVoiceCommand.ExecuteAsync(null);
        Assert.True(vm.Voice.IsRecording);
        vm.StopVoiceRecordingCommand.Execute(null);
        await recording;
        Assert.True(recorder.Disposed);
        var draft = Assert.Single(vm.AttachmentComposer.Files);
        try
        {
            Assert.True(File.Exists(draft.Path));
            Assert.False(vm.CanEditComposerText);
            Assert.False(vm.CanAddAttachments);
            var uploads = new List<(string, UploadFileType)>();
            service.UploadAttachment = (name, type, _, _) =>
            {
                uploads.Add((name, type));
                return Task.FromResult<(ErrorReturner, string?)>(uploads.Count == 1
                    ? (new(false, errorResourceKey: "Messenger_AttachmentReadFailed"), null)
                    : (new(true), "voice-id"));
            };
            var sends = new List<(string, string, long, string)>();
            service.SendAttachments = (chat, text, reply, ids) =>
            {
                sends.Add((chat, text, reply, string.Join(",", ids)));
                return Task.FromResult<(ErrorReturner, MessageModel?)>(sends.Count == 1
                    ? (new(false, errorResourceKey: "Messenger_UserUnavailable"), null)
                    : (new(true), MessengerTestDoubles.CreateMessage(51, chat, 1, text)));
            };
            await dispatcher.RunAsync(() => vm.SendCommand.ExecuteAsync(null));
            await MessengerTestDoubles.WaitForOutgoingAsync(vm, () =>
                vm.PendingMessages.SingleOrDefault()?.Status == "Messenger_AttachmentReadFailed", dispatcher);
            var bubble = Assert.Single(vm.PendingMessages);
            Assert.True(bubble.CanRetry);
            Assert.Equal("voice.ogg", bubble.Details);
            Assert.Empty(sends);
            Assert.Empty(vm.AttachmentComposer.Files);
            Assert.False(File.Exists(draft.Path));

            await dispatcher.RunAsync(() => bubble.RetryCommand.ExecuteAsync(null));
            await MessengerTestDoubles.WaitForOutgoingAsync(vm, () => bubble.Status == "Messenger_UserUnavailable", dispatcher);
            Assert.True(bubble.CanRetry);
            Assert.Single(sends);

            await dispatcher.RunAsync(() => bubble.RetryCommand.ExecuteAsync(null));
            await MessengerTestDoubles.WaitForMessageAsync(vm, 51, dispatcher);
            Assert.Equal([("voice.ogg", UploadFileType.MessageAttachmentVoice), ("voice.ogg", UploadFileType.MessageAttachmentVoice)], uploads);
            Assert.Equal([("a", "", 50L, "voice-id"), ("a", "", 50L, "voice-id")], sends);
            Assert.Empty(vm.AttachmentComposer.Files);
            Assert.False(File.Exists(draft.Path));
        }
        finally
        {
            await dispatcher.RunAsync(vm.CancelAttachmentsAsync);
        }
    }

    [Theory]
    [InlineData("cancel", null)]
    [InlineData("interrupt", "Messenger_VoiceInterrupted")]
    [InlineData("chat", null)]
    [InlineData("reset", null)]
    [InlineData("exit", null)]
    public async Task RecordingCleanupReleasesDevice(string trigger, string? error)
    {
        var recorder = new Recorder();
        var (vm, _) = await OpenAsync(recorder);
        var recording = vm.RecordVoiceCommand.ExecuteAsync(null);
        Assert.True(vm.Voice.IsRecording);
        switch (trigger)
        {
            case "cancel": await vm.CancelVoiceRecordingCommand.ExecuteAsync(null); break;
            case "interrupt": recorder.Interrupted!(); break;
            case "chat": vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b"); break;
            case "reset": vm.Reset(); break;
            case "exit": await vm.CancelAttachmentsAsync(); break;
        }
        await recording;
        Assert.True(recorder.Disposed);
        Assert.False(recorder.Stopped);
        Assert.False(vm.Voice.IsBusy);
        Assert.Equal(error, vm.Voice.Error);
        Assert.Empty(vm.AttachmentComposer.Files);
    }

    [Fact]
    public async Task LeavingMessengerCancelsRecordingAndReleasesMicrophone()
    {
        var recorder = new Recorder();
        var (vm, _) = await OpenAsync(recorder);
        var operation = vm.RecordVoiceCommand.ExecuteAsync(null);
        Assert.True(vm.Voice.IsRecording);
        try
        {
            await vm.LeaveMessengerAsync();
            Assert.True(recorder.Disposed);
            Assert.False(recorder.Stopped);
            Assert.False(vm.Voice.IsBusy);
            Assert.Empty(vm.AttachmentComposer.Files);
        }
        finally
        {
            await vm.CancelAttachmentsAsync();
            await operation;
        }
    }

    [Fact]
    public async Task LeavingMessengerPreservesFinishedVoiceDraft()
    {
        var (vm, _) = await OpenAsync(new Recorder());
        var operation = vm.RecordVoiceCommand.ExecuteAsync(null);
        vm.StopVoiceRecordingCommand.Execute(null);
        await operation;
        try
        {
            var draft = Assert.Single(vm.AttachmentComposer.Files);
            await vm.LeaveMessengerAsync();
            Assert.Same(draft, Assert.Single(vm.AttachmentComposer.Files));
            Assert.True(File.Exists(draft.Path));
        }
        finally
        {
            await vm.CancelAttachmentsAsync();
        }
    }

    [Fact]
    public async Task SwitchingChatsDeletesCompletedRecording()
    {
        var (vm, _) = await OpenAsync(new Recorder());
        var recording = vm.RecordVoiceCommand.ExecuteAsync(null);
        vm.StopVoiceRecordingCommand.Execute(null);
        await recording;
        var path = Assert.Single(vm.AttachmentComposer.Files).Path;
        try
        {
            Assert.True(File.Exists(path));
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            Assert.False(File.Exists(path));
        }
        finally { await vm.CancelAttachmentsAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortVoiceIsRejectedAndMaximumAutoStops(bool maximum)
    {
        var clock = new FakeTimeProvider();
        var recorder = new Recorder(samples: maximum ? 24_000 : 23_999);
        var (vm, _) = await OpenAsync(recorder, clock: clock);
        try
        {
            var recording = vm.RecordVoiceCommand.ExecuteAsync(null);
            if (maximum)
                clock.Advance(VoiceComposer.Maximum);
            else
                vm.StopVoiceRecordingCommand.Execute(null);
            await recording.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(recorder.Stopped);
            Assert.True(recorder.Disposed);
            Assert.Equal(maximum, vm.AttachmentComposer.HasFiles);
            Assert.Equal(maximum ? null : "Messenger_VoiceTooShort", vm.Voice.Error);
        }
        finally { await vm.CancelAttachmentsAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrDeniedMicrophoneShowsSettingsAndReturnsToReady(bool denied)
    {
        var error = denied ? (Exception)new UnauthorizedAccessException() : new InvalidOperationException();
        var (vm, _) = await OpenAsync(new Recorder(startError: error));
        await vm.RecordVoiceCommand.ExecuteAsync(null);
        Assert.Equal("Messenger_VoiceMicUnavailable", vm.Voice.Error);
        Assert.True(vm.Voice.ShowMicrophoneSettings);
        Assert.True(vm.ShowVoiceMicrophone);
        Assert.Empty(vm.AttachmentComposer.Files);
    }

    [Fact]
    public async Task PrivateReadOnlyAndEditingDoNotStartVoiceRecording()
    {
        var recorder = new Recorder();
        var service = new FakeMessengerService();
        service.Chats.AddRange([
            MessengerTestDoubles.CreateChat("private", chatType: ChatType.Private),
            MessengerTestDoubles.CreateChat("read-only", peerUserId: 3),
            MessengerTestDoubles.CreateChat("regular")
        ]);
        service.UserProfile = id => Task.FromResult<(ErrorReturner, UserData?)>(
            (new(true), new UserData { Id = id, AccountStatus = AccountStatus.Blocked }));
        service.Messages.Add(MessengerTestDoubles.CreateMessage(50, "regular", 1));
        var vm = MessengerTestDoubles.CreateViewModel(service, voiceRecorder: recorder);
        await vm.LoadAsync();

        foreach (string id in new[] { "private", "read-only", "regular" })
        {
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == id);
            if (id == "regular")
            {
                vm.StartEditMessageCommand.Execute(Assert.Single(vm.Messages));
                Assert.True(vm.IsEditing);
                vm.DraftText = "";
            }
            Assert.False(vm.ShowVoiceMicrophone);
            await vm.RecordVoiceCommand.ExecuteAsync(null);
            Assert.Null(recorder.Interrupted);
        }
    }

    [Fact]
    public async Task OneVoicePlaysAcrossChatsAndJumpOpensItsMessage()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player();
        var (vm, _) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            var items = Assert.Single(vm.Messages).FileAttachments.ToArray();
            await vm.ToggleVoicePlaybackAsync(items[0]);
            Assert.True(vm.VoicePlayer.IsVisible);
            Assert.Equal(25, vm.VoicePlayer.Progress);
            Assert.Equal("00:01 / 00:04", items[0].VoiceTime);
            var first = Assert.Single(player.Playbacks);

            await vm.ToggleVoicePlaybackAsync(items[1]);
            Assert.True(first.Disposed);
            Assert.False(items[0].IsVoiceActive);
            first.Ended(true);
            Assert.True(items[1].IsVoiceActive);

            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            Assert.True(vm.VoicePlayer.IsPlaying);
            Assert.False(player.Playbacks[1].Disposed);

            await vm.OpenPlayingVoiceCommand.ExecuteAsync(null);
            Assert.Equal("a", vm.SelectedChat!.Id);
            Assert.Equal(MessageScrollTarget.Message, vm.ScrollRequest?.Target);
            Assert.Equal(50, vm.ScrollRequest?.MessageId);
            vm.VoicePlayer.StopCommand.Execute(null);
            Assert.False(vm.VoicePlayer.IsVisible);
            Assert.True(player.Playbacks[1].Disposed);
        });
    }

    [Fact]
    public async Task CallDuringPreparationPausesVoiceAndEndingCallLeavesItPaused()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player(deferStart: true);
        var clock = new FakeTimeProvider();
        var calls = new AudioCallService(new AudioCallTests.Media(), dispatcher, clock);
        var (vm, _) = await OpenAsync(player: player, clock: clock, withVoice: true,
            dispatcher: dispatcher, calls: calls);

        await dispatcher.RunAsync(async () =>
        {
            var item = Assert.Single(vm.Messages).FileAttachments.First();
            var starting = vm.ToggleVoicePlaybackAsync(item);
            var signaling = new AudioCallTests.Signaling();
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Start(signaling);
            await signaling.Events.Writer.WriteAsync((new CallEvent
            {
                Incoming = new() { CallId = Guid.NewGuid().ToString(), CallerUserId = 2,
                    MediaType = CallMediaType.CallMediaAudio }
            }, seen));
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(calls.Current);
            Assert.True(vm.VoicePlayer.CallInProgress);
            player.CompleteStart();
            await starting;

            var playback = Assert.Single(player.Playbacks);
            Assert.True(vm.VoicePlayer.IsVisible);
            Assert.False(playback.IsPlaying);
            vm.VoicePlayer.ToggleCommand.Execute(null);
            Assert.False(playback.IsPlaying);
            await calls.HangUpAsync();
            Assert.False(vm.VoicePlayer.CallInProgress);
            Assert.False(playback.IsPlaying);

            vm.VoicePlayer.CycleSpeedCommand.Execute(null);
            Assert.Equal(1.5, playback.Speed);
            vm.VoicePlayer.CycleSpeedCommand.Execute(null);
            Assert.Equal(2, playback.Speed);
            vm.VoicePlayer.CycleSpeedCommand.Execute(null);
            Assert.Equal(1, playback.Speed);
            vm.VoicePlayer.Seek(75);
            Assert.Equal(TimeSpan.FromSeconds(3), playback.Position);

            vm.VoicePlayer.ToggleCommand.Execute(null);
            Assert.True(playback.IsPlaying);
            vm.VoicePlayer.Pause();
            Assert.False(playback.IsPlaying);
            Assert.True(vm.VoicePlayer.IsVisible);
            vm.Reset();
            Assert.True(playback.Disposed);
            await calls.StopAsync();
        });
    }

    [Fact]
    public async Task RealtimeDeletionStopsVoiceFromAnotherChat()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player();
        var realtime = new FakeRealtimeMessengerService();
        var (vm, _) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher, realtime: realtime);

        await dispatcher.RunAsync(async () =>
        {
            var item = Assert.Single(vm.Messages).FileAttachments.First();
            await vm.ToggleVoicePlaybackAsync(item);
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            realtime.Raise(new MessageUpdate("b", false, 50, MessageUpdateKind.Delete));
            Assert.True(vm.VoicePlayer.IsVisible);
            realtime.Raise(new MessageUpdate("a", false, 50, MessageUpdateKind.Delete));
            Assert.False(vm.VoicePlayer.IsVisible);
            Assert.True(Assert.Single(player.Playbacks).Disposed);
        });
    }

    [Fact]
    public async Task VoiceFromProfileKeepsPlayingAndCanJumpToItsMessage()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player();
        var (vm, service) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher);
        service.Attachments.Add(new ChatAttachmentInfo
        {
            MessageId = 50,
            Attachment = new MessageAttachment { Type = MessageAttachmentType.Voice, FileId = "voice-1" }
        });

        await dispatcher.RunAsync(async () =>
        {
            vm.Profile.VoiceTab.Reset("a");
            await vm.Profile.VoiceTab.EnsureLoadedAsync();
            vm.IsProfileVisible = true;
            vm.Profile.SelectedAttachmentTabIndex = 3;
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Profile.VoiceTab.Items));
            vm.Profile.SelectedAttachmentTabIndex = 0;
            vm.CloseProfileCommand.Execute(null);
            Assert.True(vm.VoicePlayer.IsPlaying);

            await vm.OpenPlayingVoiceCommand.ExecuteAsync(null);
            Assert.Equal(50, vm.ScrollRequest?.MessageId);
            await vm.LeaveMessengerAsync();
            Assert.False(vm.VoicePlayer.IsVisible);
            Assert.True(Assert.Single(player.Playbacks).Disposed);
        });
    }

    [Fact]
    public async Task LeavingChatWhileMicrophoneStartsDisposesLateRecording()
    {
        var recorder = new Recorder(deferStart: true);
        var (vm, _) = await OpenAsync(recorder);
        var operation = vm.RecordVoiceCommand.ExecuteAsync(null);
        Assert.True(vm.Voice.IsBusy);

        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        recorder.CompleteStart();
        await operation;

        Assert.True(recorder.Disposed);
        Assert.False(recorder.Stopped);
        Assert.False(vm.Voice.IsBusy);
        Assert.Null(vm.Voice.Error);
        Assert.Empty(vm.AttachmentComposer.Files);
    }

    [Fact]
    public async Task LeavingSessionWhileVoiceLoadsDisposesLatePlayback()
    {
        var player = new Player(deferStart: true);
        var (vm, _) = await OpenAsync(player: player, withVoice: true);
        var item = vm.Messages.Single().FileAttachments.First();
        var operation = vm.ToggleVoicePlaybackAsync(item);
        Assert.True(item.IsVoiceActive);

        vm.Reset();
        player.CompleteStart();
        await operation;

        Assert.True(player.Token.IsCancellationRequested);
        Assert.True(Assert.Single(player.Playbacks).Disposed);
        Assert.False(item.IsVoiceActive);
        Assert.Null(item.VoiceError);
    }

    [Fact]
    public async Task ResyncDeletionStopsVoice()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player();
        var realtime = new FakeRealtimeMessengerService();
        var (vm, service) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher, realtime: realtime);

        await dispatcher.RunAsync(async () =>
        {
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            service.Messages.Clear();
            realtime.Resync();
            Assert.Empty(vm.Messages);
            Assert.True(Assert.Single(player.Playbacks).Disposed);
            Assert.False(vm.VoicePlayer.IsVisible);
            vm.Reset();
        });
    }

    [Fact]
    public async Task SupersededJumpDoesNotShowError()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var lookup = new TaskCompletionSource<Chat?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, service) = await OpenAsync(player: new Player(), clock: new FakeTimeProvider(),
            withVoice: true, dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            vm.Chats.Remove(vm.Chats.Single(chat => chat.Id == "a"));
            var requests = 0;
            var firstLookup = new TaskCompletionSource<Chat?>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.FindChat = _ => ++requests == 1 ? firstLookup.Task : lookup.Task;
            var first = vm.OpenPlayingVoiceCommand.ExecuteAsync(null);
            var second = vm.OpenPlayingVoiceCommand.ExecuteAsync(null);
            firstLookup.SetResult(null);
            await first;
            Assert.Null(vm.ActionError);
            lookup.SetResult(MessengerTestDoubles.CreateChat("a"));
            await second;
            Assert.Equal(50, vm.ScrollRequest?.MessageId);
            vm.Reset();
        });
    }

    [Fact]
    public async Task JumpToDeletedVoiceReportsUnavailable()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var (vm, service) = await OpenAsync(player: new Player(), clock: new FakeTimeProvider(),
            withVoice: true, dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            service.Messages.Clear();
            await vm.OpenPlayingVoiceCommand.ExecuteAsync(null);
            Assert.Equal("Messenger_HistoryMessageUnavailable", vm.ActionError);
            Assert.NotEqual(50L, vm.ScrollRequest?.MessageId);
            vm.Reset();
        });
    }

    [Fact]
    public async Task SeekBeforeDurationIsKnownLeavesPositionUnchanged()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var player = new Player(durationKnown: false);
        var (vm, _) = await OpenAsync(player: player, clock: clock, withVoice: true, dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            var playback = Assert.Single(player.Playbacks);
            Assert.False(vm.VoicePlayer.CanSeek);
            vm.VoicePlayer.Seek(75);
            Assert.Equal(TimeSpan.FromSeconds(1), playback.Position);

            playback.Duration = TimeSpan.FromSeconds(4);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            Assert.True(vm.VoicePlayer.CanSeek);
            vm.VoicePlayer.Seek(75);
            Assert.Equal(TimeSpan.FromSeconds(3), playback.Position);
            vm.Reset();
        });
    }

    [Fact]
    public async Task SpeedPersistsForVoiceAcrossViewModelsButDoesNotAffectAudioFiles()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var store = new TestApplicationDataStore();
        var player = new Player();
        var (first, _) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher, preferences: new SettingsPreferences(store));
        await dispatcher.RunAsync(async () =>
        {
            await first.ToggleVoicePlaybackAsync(Assert.Single(first.Messages).FileAttachments.First());
            await first.VoicePlayer.CycleSpeedCommand.ExecuteAsync(null);
            Assert.Equal(1.5, Assert.Single(player.Playbacks).Speed);
            first.Reset();
        });

        var (second, _) = await OpenAsync(player: player, clock: new FakeTimeProvider(), withVoice: true,
            dispatcher: dispatcher, preferences: new SettingsPreferences(store), withAudio: true);
        await dispatcher.RunAsync(async () =>
        {
            var items = Assert.Single(second.Messages).FileAttachments;
            await second.ToggleVoicePlaybackAsync(items.Last());
            Assert.Equal(1, player.Playbacks.Last().Speed);
            Assert.False(second.VoicePlayer.CanChangeSpeed);
            await second.VoicePlayer.CycleSpeedCommand.ExecuteAsync(null);
            Assert.Equal(1, player.Playbacks.Last().Speed);

            await second.ToggleVoicePlaybackAsync(items.First());
            Assert.True(second.VoicePlayer.CanChangeSpeed);
            Assert.Equal(1.5, player.Playbacks.Last().Speed);
            second.Reset();
        });
    }

    [Fact]
    public async Task ScrubHoldsProgressAndSeeksOnceOnRelease()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var player = new Player();
        var (vm, _) = await OpenAsync(player: player, clock: clock, withVoice: true, dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            await vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            var playback = Assert.Single(player.Playbacks);
            vm.VoicePlayer.BeginScrub();
            playback.AdvanceTo(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromMilliseconds(200));
            vm.VoicePlayer.Seek(60);
            Assert.Equal(25, vm.VoicePlayer.Progress);
            Assert.Equal(TimeSpan.FromSeconds(2), playback.Position);

            vm.VoicePlayer.EndScrub(75);
            Assert.Equal(TimeSpan.FromSeconds(3), playback.Position);
            Assert.Equal(75, vm.VoicePlayer.Progress);
            vm.Reset();
        });
    }

    [Fact]
    public async Task StartingVoicePausesVideoAndStartingVideoPausesVoice()
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var player = new Player(deferStart: true);
        var (vm, _) = await OpenAsync(player: player, clock: new FakeTimeProvider(),
            withVoice: true, dispatcher: dispatcher);

        await dispatcher.RunAsync(async () =>
        {
            var starting = vm.ToggleVoicePlaybackAsync(Assert.Single(vm.Messages).FileAttachments.First());
            vm.OnVideoPlaybackStarted();
            player.CompleteStart();
            await starting;
            var playback = Assert.Single(player.Playbacks);
            Assert.False(playback.IsPlaying);

            var videoPlaying = true;
            vm.VoicePlayer.Starting += () =>
            {
                Assert.False(playback.IsPlaying);
                videoPlaying = false;
            };
            vm.VoicePlayer.ToggleCommand.Execute(null);
            Assert.True(playback.IsPlaying);
            Assert.False(videoPlaying);

            vm.OnVideoPlaybackStarted();
            Assert.False(playback.IsPlaying);
            Assert.True(vm.VoicePlayer.IsVisible);
            vm.Reset();
        });
    }

    private static async Task<(MessengerViewModel Vm, FakeMessengerService Service)> OpenAsync(
        IVoiceRecorder? recorder = null, IVoicePlayer? player = null, TimeProvider? clock = null, bool withVoice = false,
        InlineUiDispatcher? dispatcher = null, FakeRealtimeMessengerService? realtime = null,
        AudioCallService? calls = null, ISettingsPreferences? preferences = null, bool withAudio = false)
    {
        dispatcher ??= new InlineUiDispatcher(serializeContinuations: true);
        var service = new FakeMessengerService();
        service.Chats.AddRange([MessengerTestDoubles.CreateChat("a"), MessengerTestDoubles.CreateChat("b")]);
        var message = MessengerTestDoubles.CreateMessage(50, "a", 2);
        if (withVoice)
        {
            message.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Voice, FileId = "voice-1" });
            message.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Voice, FileId = "voice-2" });
            if (withAudio)
                message.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Audio, FileId = "audio" });
            service.FileUrl = id => Task.FromResult($"https://example.invalid/{id}.ogg");
        }
        service.Messages.Add(message);
        var vm = MessengerTestDoubles.CreateViewModel(service, realtime: realtime, dispatcher: dispatcher, timeProvider: clock,
            voiceRecorder: recorder, voicePlayer: player, calls: calls, stickerPreferences: preferences);
        await dispatcher.RunAsync(async () =>
        {
            await vm.LoadAsync();
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        });
        return (vm, service);
    }

    private static MemoryStream Wave(int samples)
    {
        var wave = new MemoryStream();
        VoiceOggCodec.WriteWaveHeader(wave, 1, samples * 2);
        using var writer = new BinaryWriter(wave, System.Text.Encoding.UTF8, leaveOpen: true);
        for (int i = 0; i < samples; i++)
            writer.Write((short)(8000 * Math.Sin(i * 2 * Math.PI / 120)));
        wave.Position = 0;
        return wave;
    }

    internal sealed class Recorder(int samples = 24_017, Exception? startError = null, bool deferStart = false) : IVoiceRecorder, IVoiceRecording
    {
        private readonly TaskCompletionSource<IVoiceRecording> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? Interrupted { get; private set; }
        public bool Disposed { get; private set; }
        public bool Stopped { get; private set; }

        public Task<IVoiceRecording> StartAsync(Action interrupted, CancellationToken cancellationToken)
        {
            Interrupted = interrupted;
            if (startError is not null)
                return Task.FromException<IVoiceRecording>(startError);
            return deferStart ? _start.Task : Task.FromResult<IVoiceRecording>(this);
        }

        public void CompleteStart() => _start.SetResult(this);

        public Task<Stream> StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.FromResult<Stream>(Wave(samples));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Player(bool deferStart = false, bool durationKnown = true) : IVoicePlayer
    {
        private readonly TaskCompletionSource<IVoicePlayback> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Playback> Playbacks { get; } = [];
        public CancellationToken Token { get; private set; }

        public void CompleteStart() => _start.SetResult(Playbacks.Single());

        public Task<IVoicePlayback> PrepareAsync(string url, bool isOpus, Action<bool> ended, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            var playback = new Playback(ended) { Duration = durationKnown ? TimeSpan.FromSeconds(4) : TimeSpan.Zero };
            Playbacks.Add(playback);
            return deferStart ? _start.Task : Task.FromResult<IVoicePlayback>(playback);
        }
    }

    private sealed class Playback(Action<bool> ended) : IVoicePlayback
    {
        public Action<bool> Ended => ended;
        public TimeSpan Position { get; private set; } = TimeSpan.FromSeconds(1);
        public bool CanSeek => true;
        public bool IsPlaying { get; private set; }
        public double Speed { get; private set; } = 1;

        public void Play(double speed)
        {
            Speed = speed;
            IsPlaying = true;
        }

        public void Pause() => IsPlaying = false;
        public void AdvanceTo(TimeSpan position) => Position = position;
        public void Seek(TimeSpan position) => Position = position;
        public void SetSpeed(double speed) => Speed = speed;
        public TimeSpan Duration { get; set; }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
