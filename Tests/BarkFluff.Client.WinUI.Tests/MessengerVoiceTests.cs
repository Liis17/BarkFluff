using System.Buffers.Binary;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Files.Services;
using BarkFluff.Proto.Files;
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
        var (vm, service) = await OpenAsync(recorder);
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
                    ? (new(false, "failed"), null)
                    : (new(true), "voice-id"));
            };
            var sends = new List<(string, string, long, string)>();
            service.SendAttachments = (chat, text, reply, ids) =>
            {
                sends.Add((chat, text, reply, string.Join(",", ids)));
                return Task.FromResult<(ErrorReturner, MessageModel?)>(sends.Count == 1
                    ? (new(false, "failed"), null)
                    : (new(true), MessengerTestDoubles.CreateMessage(51, chat, 1, text)));
            };
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Empty(sends);
            Assert.Same(draft, Assert.Single(vm.AttachmentComposer.Files));
            Assert.True(File.Exists(draft.Path));
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Same(draft, Assert.Single(vm.AttachmentComposer.Files));
            Assert.True(File.Exists(draft.Path));
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Equal([("voice.ogg", UploadFileType.MessageAttachmentVoice), ("voice.ogg", UploadFileType.MessageAttachmentVoice)], uploads);
            Assert.Equal([("a", "", 50L, "voice-id"), ("a", "", 50L, "voice-id")], sends);
            Assert.Empty(vm.AttachmentComposer.Files);
            Assert.False(File.Exists(draft.Path));
        }
        finally
        {
            await vm.CancelAttachmentsAsync();
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
    public async Task OnlyOneVoicePlaysAndChatSwitchStopsIt()
    {
        var player = new Player();
        var clock = new FakeTimeProvider();
        var (vm, _) = await OpenAsync(player: player, clock: clock, withVoice: true);
        var items = Assert.Single(vm.Messages).FileAttachments.ToArray();
        Assert.Equal(2, items.Length);
        await vm.ToggleVoicePlaybackAsync(items[0]);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(items[0].IsVoiceActive);
        Assert.Equal(25, items[0].VoiceProgress);
        Assert.Equal("00:01 / 00:04", items[0].VoiceTime);
        var first = Assert.Single(player.Playbacks);

        await vm.ToggleVoicePlaybackAsync(items[1]);
        Assert.True(first.Disposed);
        Assert.False(items[0].IsVoiceActive);
        Assert.True(items[1].IsVoiceActive);
        first.Ended(true);
        Assert.True(items[1].IsVoiceActive);

        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        Assert.True(player.Playbacks[1].Disposed);
        Assert.False(items[1].IsVoiceActive);
        Assert.Equal(0, items[0].VoiceProgress);
        Assert.Equal("00:00 / 00:04", items[0].VoiceTime);
    }

    [Fact]
    public async Task ClosingProfileOrChangingTabStopsOnlyProfilePlayback()
    {
        var player = new Player();
        var (vm, _) = await OpenAsync(player: player, withVoice: true);
        var items = Assert.Single(vm.Messages).FileAttachments.ToArray();
        vm.Profile.VoiceTab.Items.Add(items[0]);
        vm.IsProfileVisible = true;
        vm.Profile.SelectedAttachmentTabIndex = 3;

        await vm.ToggleVoicePlaybackAsync(items[0]);
        vm.Profile.SelectedAttachmentTabIndex = 0;
        Assert.True(player.Playbacks[0].Disposed);

        vm.Profile.SelectedAttachmentTabIndex = 3;
        await vm.ToggleVoicePlaybackAsync(items[0]);
        vm.CloseProfileCommand.Execute(null);
        Assert.True(player.Playbacks[1].Disposed);

        await vm.ToggleVoicePlaybackAsync(items[1]);
        vm.IsProfileVisible = true;
        vm.Profile.SelectedAttachmentTabIndex = 0;
        vm.CloseProfileCommand.Execute(null);
        Assert.False(player.Playbacks[2].Disposed);
        await vm.LeaveMessengerAsync();
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

    private static async Task<(MessengerViewModel Vm, FakeMessengerService Service)> OpenAsync(
        IVoiceRecorder? recorder = null, IVoicePlayer? player = null, TimeProvider? clock = null, bool withVoice = false)
    {
        var service = new FakeMessengerService();
        service.Chats.AddRange([MessengerTestDoubles.CreateChat("a"), MessengerTestDoubles.CreateChat("b")]);
        var message = MessengerTestDoubles.CreateMessage(50, "a", 2);
        if (withVoice)
        {
            message.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Voice, FileId = "voice-1" });
            message.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Voice, FileId = "voice-2" });
            service.FileUrl = id => Task.FromResult($"https://example.invalid/{id}.ogg");
        }
        service.Messages.Add(message);
        var vm = MessengerTestDoubles.CreateViewModel(service, timeProvider: clock, voiceRecorder: recorder, voicePlayer: player);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
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

    private sealed class Recorder(int samples = 24_017, Exception? startError = null, bool deferStart = false) : IVoiceRecorder, IVoiceRecording
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

    private sealed class Player(bool deferStart = false) : IVoicePlayer
    {
        private readonly TaskCompletionSource<IVoicePlayback> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Playback> Playbacks { get; } = [];
        public CancellationToken Token { get; private set; }

        public void CompleteStart() => _start.SetResult(Playbacks.Single());

        public Task<IVoicePlayback> PlayAsync(string url, bool isOpus, Action<bool> ended, CancellationToken cancellationToken)
        {
            Assert.True(isOpus);
            Token = cancellationToken;
            var playback = new Playback(ended);
            Playbacks.Add(playback);
            return deferStart ? _start.Task : Task.FromResult<IVoicePlayback>(playback);
        }
    }

    private sealed class Playback(Action<bool> ended) : IVoicePlayback
    {
        public Action<bool> Ended => ended;
        public TimeSpan Position => TimeSpan.FromSeconds(1);
        public TimeSpan Duration => TimeSpan.FromSeconds(4);
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
