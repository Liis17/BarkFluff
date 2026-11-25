using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly VoicePlayback _voicePlayback;
    public VoiceComposer Voice { get; }
    public VoicePlayback VoicePlayer => _voicePlayback;

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

    public Task ToggleVoicePlaybackAsync(MessageAttachmentItemViewModel item)
    {
        if (!IsCurrent(_realtimeSession) || Voice.IsBusy || _calls?.Current is not null)
            return Task.CompletedTask;

        if (Profile.VoiceTab.Items.Contains(item))
            return _voicePlayback.ToggleAsync(item, item.VoiceTarget);

        var message = Messages.FirstOrDefault(message => message.FileAttachments.Contains(item));
        return SelectedChat is { IsPrivate: false } chat && message is not null
            ? _voicePlayback.ToggleAsync(item, (chat.Id, message.Id))
            : Task.CompletedTask;
    }

    public void OnVideoPlaybackStarted() => _voicePlayback.Pause();

    [RelayCommand]
    private async Task OpenPlayingVoiceAsync()
    {
        if (_realtimeSession is not { } session || !IsCurrent(session) ||
            _voicePlayback.Target is not { } target || _voicePlayback.Lifetime is not { } token)
            return;

        var version = _chatVersion;
        var opening = OpenChatByIdAsync(session, target.ChatId, token);
        var activation = _notificationActivation;
        if (await opening && activation == _notificationActivation &&
            IsCurrent(session) && !token.IsCancellationRequested &&
            SelectedChat is { IsPrivate: false } chat && chat.Id == target.ChatId)
        {
            SecretChats.Close();
            await NavigateToMessageAsync(target.MessageId);
        }
        else if (activation == _notificationActivation && IsCurrent(session) &&
            !token.IsCancellationRequested && version == _chatVersion)
        {
            ActionError = _localization.GetString("Messenger_HistoryMessageUnavailable");
        }
    }

    public Task LeaveMessengerAsync(bool clearAttachments = false)
    {
        MediaViewer.Close();
        _voicePlayback.Stop();
        return clearAttachments ? CancelAttachmentsAsync() : Voice.CancelAsync();
    }
}
