using System.ComponentModel;

using BarkFluff.Client.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly HashSet<long> _newMessagesBelow = [];
    private readonly HashSet<long> _shownUnreadMessages = [];
    private bool _jumpedToUnread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JumpDownName))]
    [NotifyPropertyChangedFor(nameof(HasNewMessagesBelow))]
    private int _newMessagesBelowCount;

    public bool HasNewMessagesBelow => NewMessagesBelowCount > 0;
    public string JumpDownName => HasNewMessagesBelow
        ? string.Format(_localization.GetString("Messenger_JumpDownCount"), NewMessagesBelowCount)
        : _localization.GetString("Messenger_JumpDown");
    public bool IsJumpDownVisible => _feedLoaded && (!_feedIncludesTail || !_isFeedAtBottom);

    private void ResetFeedNavigation(ChatItemViewModel? previous, ChatItemViewModel? current)
    {
        if (previous is not null)
            previous.PropertyChanged -= OnFeedChatChanged;
        if (current is not null)
            current.PropertyChanged += OnFeedChatChanged;
        _shownUnreadMessages.Clear();
        _isFeedAtBottom = true;
        ClearNewMessagesBelow();
        OnPropertyChanged(nameof(IsJumpDownVisible));
    }

    private void OnFeedChatChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChatItemViewModel.UnreadCount) &&
            sender is ChatItemViewModel { UnreadCount: 0 })
        {
            _newMessagesBelow.Clear();
            NewMessagesBelowCount = 0;
        }
    }

    private void ClearNewMessagesBelow()
    {
        _jumpedToUnread = false;
        _newMessagesBelow.Clear();
        NewMessagesBelowCount = 0;
    }

    private void CountNewMessageBelow(long messageId)
    {
        if ((!_feedIncludesTail || !_isFeedAtBottom) && _newMessagesBelow.Add(messageId))
            NewMessagesBelowCount = _newMessagesBelow.Count;
    }

    private void ForgetNewMessageBelow(long messageId)
    {
        if (_newMessagesBelow.Remove(messageId))
            NewMessagesBelowCount = _newMessagesBelow.Count;
    }

    private void ForgetPrivateMessagesRead(long lastReadId)
    {
        if (_newMessagesBelow.RemoveWhere(id => id <= lastReadId) > 0)
            NewMessagesBelowCount = _newMessagesBelow.Count;
    }

    [RelayCommand]
    private async Task JumpDownAsync()
    {
        var chat = SelectedChat;
        var session = _realtimeSession;
        var version = _chatVersion;
        if (!IsJumpDownVisible || chat is null || !IsOpen(session, chat, version))
            return;

        var firstUnread = chat.FirstUnreadMessageId;
        if (!_jumpedToUnread && chat.UnreadCount > 0 && firstUnread > 0 &&
            !_shownUnreadMessages.Contains(firstUnread))
        {
            _jumpedToUnread = true;
            await NavigateToMessageAsync(firstUnread, fallbackToTail: true);
            return;
        }

        await ShowOutgoingTailAsync();
    }
}
