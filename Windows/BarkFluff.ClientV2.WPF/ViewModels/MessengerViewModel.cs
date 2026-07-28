using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class MessengerViewModel : ObservableObject
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;

    public MessengerViewModel(WebApiClient webApi, IClientSession session)
    {
        _webApi = webApi;
        _session = session;
    }

    public ObservableCollection<ChatItemViewModel> Chats { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    [ObservableProperty] private ChatItemViewModel? _selectedChat;
    [ObservableProperty] private string _draftText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;

    public async Task LoadAsync()
    {
        var parameters = _session.CurrentConnection?.ConnectionParameters;
        if (parameters is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _webApi.GetChats(parameters);
            if (!result.error.IsSuccess || result.chats is null)
            {
                return;
            }

            Chats.Clear();
            foreach (var chat in result.chats.OrderByDescending(chat => chat.LastActivityAt?.ToDateTimeOffset()))
            {
                Chats.Add(new ChatItemViewModel(chat));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedChatChanged(ChatItemViewModel? value)
    {
        Messages.Clear();
        if (value is not null)
        {
            _ = LoadMessagesAsync(value);
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var parameters = _session.CurrentConnection?.ConnectionParameters;
        if (parameters is null || SelectedChat is null || string.IsNullOrWhiteSpace(DraftText))
        {
            return;
        }

        var result = await _webApi.SendMessage(
            parameters,
            (false, SelectedChat.Id),
            new ForwardingLetter { Text = DraftText.Trim() });
        if (result.error.IsSuccess && result.message is not null)
        {
            AddMessage(result.message, parameters.UserId);
            DraftText = string.Empty;
        }
    }

    private async Task LoadMessagesAsync(ChatItemViewModel chat)
    {
        var parameters = _session.CurrentConnection?.ConnectionParameters;
        if (parameters is null)
        {
            return;
        }

        var result = await _webApi.GetMessagesWithOffset(parameters, chat.Id, 0, 50, 0);
        if (!result.error.IsSuccess || result.messages is null || SelectedChat?.Id != chat.Id)
        {
            return;
        }

        Messages.Clear();
        foreach (var message in result.messages.OrderBy(message => message.MessageId))
        {
            AddMessage(message, parameters.UserId);
        }
    }

    private void AddMessage(MessageModel message, long currentUserId)
    {
        if (Messages.Any(item => item.Id == message.MessageId))
        {
            return;
        }

        Messages.Add(new MessageItemViewModel(message, message.SenderId == currentUserId));
    }
}

public sealed class ChatItemViewModel(Chat chat)
{
    public string Id { get; } = chat.Id;
    public string Title { get; } = string.IsNullOrWhiteSpace(chat.Title) ? "Chat" : chat.Title;
    public string Preview { get; } = chat.LastMessage?.Content?.Text ?? string.Empty;
    public long UnreadCount { get; } = chat.CountUnread;
    public bool IsPrivate { get; } = chat.ChatType == BarkFluff.Proto.Shared.ChatType.Private;
}

public sealed class MessageItemViewModel(MessageModel message, bool isMine)
{
    public long Id { get; } = message.MessageId;
    public string Text { get; } = message.Text;
    public bool IsMine { get; } = isMine;
    public DateTimeOffset SentAt { get; } = message.SentAt.ToDateTimeOffset();
}
