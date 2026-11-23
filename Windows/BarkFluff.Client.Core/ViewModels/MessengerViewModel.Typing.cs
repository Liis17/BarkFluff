using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly IChatTypingService? _typing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypingLabelVisible), nameof(IsPresenceLabelVisible))]
    private string _typingLabel = string.Empty;

    public bool IsTypingLabelVisible => !IsReconnecting && TypingLabel.Length > 0;

    private void WatchTyping(ChatItemViewModel? chat)
    {
        ResetTyping();
        if (_typing is null || chat?.Definition.ChatType != ChatType.Regular || !IsCurrent(_realtimeSession))
        {
            return;
        }

        _typing.Changed += UpdateTypingLabel;
        _typing.Watch(chat.Id);
    }

    private void ResetTyping()
    {
        if (_typing is null)
        {
            return;
        }

        _typing.Changed -= UpdateTypingLabel;
        _typing.Watch(null);
        TypingLabel = string.Empty;
    }

    partial void OnDraftTextChanged(string value)
    {
        SaveComposerDraft();
        if (!_applyingDraft && SelectedChat is { CanSend: true, Definition.ChatType: ChatType.Regular } &&
            !IsEditing && IsCurrent(_realtimeSession))
        {
            _typing?.Input(!string.IsNullOrWhiteSpace(value));
        }
    }

    partial void OnEditingMessageChanged(MessageItemViewModel? value)
    {
        NotifyVoiceComposerChanged();
        if (value is not null)
        {
            _typing?.StopSending();
        }
    }

    private void UpdateTypingLabel()
    {
        var ids = _typing!.TypingUserIds;
        if (SelectedChat is not { } chat || !IsCurrent(_realtimeSession) || ids.Count == 0)
        {
            TypingLabel = string.Empty;
            return;
        }
        if (!chat.Definition.IsGroupChat)
        {
            TypingLabel = _localization.GetString("Messenger_Typing");
            return;
        }

        var names = ids.Count > 3 ? [] : ids.Select(FindTypingName).OfType<string>().ToList();
        if (names.Count == 0)
        {
            names.Add(_localization.GetString("Messenger_TypingSomeone"));
        }
        else if (names.Count < ids.Count)
        {
            names.Add(_localization.GetString("Messenger_TypingUnknownParticipant"));
        }

        var format = names.Count > 1 ? "Messenger_TypingNames" : "Messenger_TypingName";
        TypingLabel = string.Format(_localization.GetString(format), string.Join(", ", names));
    }

    private string? FindTypingName(long userId)
    {
        if (userId == 0)
        {
            return null;
        }

        var peer = Chats.FirstOrDefault(chat => chat.PeerUserId == userId);
        if (peer is not null)
        {
            var firstName = peer.FirstName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstName is not null || !string.IsNullOrWhiteSpace(peer.Username))
            {
                return firstName ?? peer.Username;
            }
        }
        return SearchResults.FirstOrDefault(user => user.UserId == userId)?.DisplayName;
    }
}
