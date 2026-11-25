using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Calls;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly AudioCallService? _calls;
    private readonly CallSignaling? _callSignaling;
    private Guid _lookupCallId;

    private CallHistoryViewModel? _callHistory;

    public bool ShowCallHistory => _calls?.Available == true;
    public bool ShowAudioCall => ShowCallHistory && AudioCallChat is not null;
    public bool CanStartAudioCall => ShowAudioCall && _calls?.Current is null &&
        (AudioCallChat?.Definition.IsGroupChat != true || SelectedCallGroup is not null);
    public bool CanJoinGroupCall => CanStartAudioCall && SelectedCallGroup is { } group &&
        _calls?.JoinableCall?.ChatId == group;

    private ChatItemViewModel? AudioCallChat => SelectedChat is { Definition.ChatType: ChatType.Regular } chat &&
        (chat.Definition.IsGroupChat || (chat.PeerUserId > 0 && chat.PeerUserId != _messenger.CurrentUserId))
        ? chat : null;

    private Guid? SelectedCallGroup => AudioCallChat is { Definition.IsGroupChat: true } chat &&
        Guid.TryParse(chat.Id, out var id) && id != Guid.Empty ? id : null;

    [RelayCommand]
    private Task StartAudioCallAsync() => StartChatCallAsync(CallMediaType.CallMediaAudio);

    [RelayCommand]
    private Task StartVideoCallAsync() => StartChatCallAsync(CallMediaType.CallMediaVideo);

    private Task StartChatCallAsync(CallMediaType type) => CanStartAudioCall && AudioCallChat is { } chat
        ? _calls!.InitiateAsync(chat.PeerUserId ?? 0, chat.Title, type, SelectedCallGroup)
        : Task.CompletedTask;

    [RelayCommand]
    private Task JoinGroupCallAsync() => CanJoinGroupCall && AudioCallChat is { } chat
        ? _calls!.JoinAsync(chat.Title) : Task.CompletedTask;

    private void ChangeCallChat()
    {
        _calls?.WatchGroup(SelectedCallGroup);
        NotifyAudioCallButton();
    }

    public CallHistoryViewModel? OpenCallHistory()
    {
        CloseCallHistory();
        if (ShowCallHistory && _callSignaling?.Open() is { IsAdvertised: true } signaling)
            _callHistory = new(signaling, FormatCallHistoryEntry);
        return _callHistory;
    }

    public void CloseCallHistory(CallHistoryViewModel? history = null)
    {
        if (history is not null && history != _callHistory)
            return;
        _callHistory?.Dispose();
        _callHistory = null;
    }

    public Task CallBackAsync(CallHistoryViewModel history, CallHistoryEntry entry)
    {
        if (history != _callHistory || !history.IsOpen || !history.Items.Contains(entry) ||
            _calls is not { Available: true, Current: null })
            return Task.CompletedTask;
        var item = entry.Call;
        if (item.IsGroup)
            return Guid.TryParse(item.ChatId, out var group) && group != Guid.Empty
                ? _calls.InitiateAsync(0, entry.Title, item.MediaType, group) : Task.CompletedTask;
        return item.PeerUserId > 0 ? _calls.InitiateAsync(item.PeerUserId, entry.Title, item.MediaType) : Task.CompletedTask;
    }

    private CallHistoryEntry FormatCallHistoryEntry(CallHistoryItem item, Guid id)
    {
        var title = item.IsGroup
            ? Chats.FirstOrDefault(chat => string.Equals(chat.Id, item.ChatId, StringComparison.OrdinalIgnoreCase))?.Title ??
              _localization.GetString("Calls_Group")
            : Chats.FirstOrDefault(chat => chat.PeerUserId == item.PeerUserId)?.Title ??
              string.Format(_localization.GetString("Calls_Peer"), item.PeerUserId);
        var direction = _localization.GetString(item.Direction == CallDirection.Incoming && item.AnsweredAt is null
            ? "Calls_HistoryMissed" : item.Direction == CallDirection.Incoming
                ? "Calls_HistoryIncoming" : "Calls_HistoryOutgoing");
        var type = _localization.GetString(item.MediaType == CallMediaType.CallMediaVideo ? "Calls_Video" : "Calls_Start");
        var date = item.StartedAt.ToDateTimeOffset().ToLocalTime().ToString("g");
        var duration = item.DurationSeconds > 0 && item.DurationSeconds <= int.MaxValue
            ? " · " + TimeSpan.FromSeconds(item.DurationSeconds).ToString("g") : string.Empty;
        return new(id, item, title,
            direction + " · " + type + " · " + date + duration);
    }

    private void OnAudioCallChanged()
    {
        _uiDispatcher.Post(() => _voicePlayback.SetCallInProgress(_calls?.Current is not null));
        NotifyAudioCallButton();
        if (_calls?.Current is not { PeerName: null } call || call.Id == _lookupCallId ||
            _realtimeSession is not { } session)
            return;

        _lookupCallId = call.Id;
        if (call.ChatId is { } group)
        {
            _calls.SetPeerName(call.Id, Chats.FirstOrDefault(item => string.Equals(item.Id, group.ToString("D"), StringComparison.OrdinalIgnoreCase))?.Title ??
                _localization.GetString("Calls_Group"));
            return;
        }
        var chat = Chats.FirstOrDefault(item => item.PeerUserId == call.PeerId);
        if (chat is not null)
            _calls.SetPeerName(call.Id, chat.Title);
        else
            _ = ResolveCallerAsync(session, call);
    }

    private async Task ResolveCallerAsync(RealtimeSession session, AudioCallStatus call)
    {
        try
        {
            var (error, user) = await _messenger.GetUserDataAsync(call.PeerId, session.Lifetime.Token);
            if (IsCurrent(session) && error?.IsSuccess == true && user is not null)
                _calls?.SetPeerName(call.Id, (user.FirstName + " " + user.LastName).Trim());
        }
        catch (Exception)
        {
            // До загрузки профиля плашка показывает идентификатор собеседника.
        }
    }

    private void NotifyAudioCallButton()
    {
        OnPropertyChanged(nameof(ShowCallHistory));
        OnPropertyChanged(nameof(CanJoinGroupCall));
        OnPropertyChanged(nameof(ShowAudioCall));
        OnPropertyChanged(nameof(CanStartAudioCall));
    }
}
