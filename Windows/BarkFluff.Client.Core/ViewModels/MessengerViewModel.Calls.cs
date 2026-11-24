using BarkFluff.Client.Core.Services.Calls;
using BarkFluff.Proto.Shared;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly AudioCallService? _calls;
    private readonly CallSignaling? _callSignaling;
    private Guid _lookupCallId;

    public bool ShowAudioCall => _calls?.Available == true && AudioCallChat is not null;
    public bool CanStartAudioCall => ShowAudioCall && _calls?.Current is null;

    private ChatItemViewModel? AudioCallChat => SelectedChat is { PeerUserId: > 0 } chat &&
        chat.Definition.ChatType == ChatType.Regular && !chat.Definition.IsGroupChat &&
        chat.PeerUserId != _messenger.CurrentUserId ? chat : null;

    [RelayCommand]
    private Task StartAudioCallAsync() => AudioCallChat is { PeerUserId: { } peerId } chat && CanStartAudioCall
        ? _calls!.InitiateAsync(peerId, chat.Title) : Task.CompletedTask;

    private void OnAudioCallChanged()
    {
        NotifyAudioCallButton();
        if (_calls?.Current is not { PeerName: null } call || call.Id == _lookupCallId ||
            _realtimeSession is not { } session)
            return;

        _lookupCallId = call.Id;
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
        OnPropertyChanged(nameof(ShowAudioCall));
        OnPropertyChanged(nameof(CanStartAudioCall));
    }
}
