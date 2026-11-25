using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly VoicePlayback _voicePlayback;
    public VoiceComposer Voice { get; }

    public bool ShowVoiceMicrophone => Voice.IsAvailable && CanAttach && !Voice.IsBusy &&
        !AttachmentComposer.HasFiles && !SendCommand.IsRunning && string.IsNullOrWhiteSpace(DraftText);
    public bool ShowComposerSend => !ShowVoiceMicrophone && !Voice.IsBusy;
    public bool CanEditComposerText => SelectedChat?.CanSend == true && !Voice.IsBusy && !AttachmentComposer.HasVoice;

    private void NotifyVoiceComposerChanged()
    {
        OnPropertyChanged(nameof(ShowVoiceMicrophone));
        OnPropertyChanged(nameof(ShowComposerSend));
        OnPropertyChanged(nameof(CanEditComposerText));
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(CanAddAttachments));
    }

    [RelayCommand]
    private Task RecordVoiceAsync()
    {
        if (!ShowVoiceMicrophone || SelectedChat is not { } chat)
            return Task.CompletedTask;
        var session = _realtimeSession;
        var version = _chatVersion;
        var token = AttachmentComposer.ScopeToken;
        bool Current() => IsOpen(session, chat, version) && !token.IsCancellationRequested &&
            !chat.IsPrivate && chat.CanSend && !IsEditing;
        _voicePlayback.Stop();
        return Voice.RecordAsync(Current, (file, canStage) =>
            AttachmentComposer.AddAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>([file]), canStage));
    }

    [RelayCommand]
    private void StopVoiceRecording() => Voice.Stop();

    [RelayCommand]
    private Task CancelVoiceRecordingAsync() => Voice.IsBusy ? CancelAttachmentsAsync() : Task.CompletedTask;

    public Task ToggleVoicePlaybackAsync(MessageAttachmentItemViewModel item) =>
        _realtimeSession is not null && !Voice.IsBusy
            ? _voicePlayback.ToggleAsync(item, Profile.VoiceTab.Items.Contains(item))
            : Task.CompletedTask;

    partial void OnIsProfileVisibleChanged(bool value)
    {
        if (!value)
            _voicePlayback.StopProfilePlayback();
    }

    public Task LeaveMessengerAsync()
    {
        MediaViewer.Close();
        _voicePlayback.Stop();
        return Voice.CancelAsync();
    }
}
