using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.Globalization;

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

            var chats = result.chats.OrderByDescending(chat => chat.LastActivityAt?.ToDateTimeOffset());
            var items = await Task.WhenAll(chats.Select(chat => CreateChatItemAsync(chat, parameters)));

            Chats.Clear();
            foreach (var item in items)
            {
                Chats.Add(item);
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
            Messages.Add(await CreateMessageItemAsync(result.message, parameters.UserId, parameters));
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
            Messages.Add(await CreateMessageItemAsync(message, parameters.UserId, parameters));
        }
    }

    private async Task<ChatItemViewModel> CreateChatItemAsync(Chat chat, GlobalParam parameters)
    {
        var title = string.IsNullOrWhiteSpace(chat.Title) ? "Chat" : chat.Title;
        var firstName = string.Empty;
        var lastName = string.Empty;
        var avatarUrl = await ResolveFileUrlAsync(parameters, chat.Picture);

        if (!chat.IsGroupChat)
        {
            var otherMember = chat.Members.FirstOrDefault(member => member.UserId != parameters.UserId);
            if (otherMember is not null)
            {
                var userResult = await _webApi.GetUserData(parameters, otherMember.UserId);
                if (userResult.Error.IsSuccess && userResult.Data is not null)
                {
                    firstName = userResult.Data.FirstName;
                    lastName = userResult.Data.LastName;
                    title = string.Join(' ', new[] { firstName, lastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
                    title = string.IsNullOrWhiteSpace(title) ? chat.Title : title;
                    avatarUrl = userResult.Data.ProfilePicturePreviewUrl;
                }
            }
        }

        return new ChatItemViewModel(chat, title, firstName, lastName, avatarUrl);
    }

    private async Task<MessageItemViewModel> CreateMessageItemAsync(MessageModel message, long currentUserId, GlobalParam parameters)
    {
        var attachments = await Task.WhenAll(message.Attachments
            .Select(attachment => CreateAttachmentItemAsync(attachment, parameters)));

        return new MessageItemViewModel(message, message.SenderId == currentUserId, attachments, currentUserId);
    }

    private async Task<MessageAttachmentItemViewModel> CreateAttachmentItemAsync(AttachmentsModel attachment, GlobalParam parameters)
    {
        var previewUrl = attachment.PreviewUrl;
        if (attachment.Type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif or MessageAttachmentType.Sticker
            && string.IsNullOrWhiteSpace(previewUrl))
        {
            previewUrl = await ResolveFileUrlAsync(parameters, attachment.PreviewFileId);
        }

        var fileUrl = await ResolveFileUrlAsync(parameters, attachment.FileId);
        return new MessageAttachmentItemViewModel(
            attachment.Type,
            previewUrl,
            fileUrl,
            attachment.FileName,
            attachment.Size);
    }

    private async Task<string> ResolveFileUrlAsync(GlobalParam parameters, string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return string.Empty;
        }

        var result = await _webApi.GetFile(parameters, fileId);
        return result.error.IsSuccess ? result.url ?? string.Empty : string.Empty;
    }
}

public sealed partial class ChatItemViewModel : ObservableObject
{
    public ChatItemViewModel(Chat chat, string title, string firstName, string lastName, string avatarUrl)
    {
        Id = chat.Id;
        Title = string.IsNullOrWhiteSpace(title) ? "Chat" : title;
        FirstName = firstName;
        LastName = lastName;
        AvatarUrl = avatarUrl;
        IsPrivate = chat.ChatType == ChatType.Private;
        Preview = IsPrivate ? string.Empty : CreatePreview(chat.LastMessage?.Content?.Text ?? string.Empty);
        UnreadCount = chat.CountUnread;
        FirstUnreadMessageId = chat.FirstUnreadMessageId;
        LastMessageAt = chat.LastActivityAt?.ToDateTimeOffset();
    }

    public string Id { get; }
    public string Title { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string AvatarUrl { get; }
    public string Preview { get; }
    [ObservableProperty] private long _unreadCount;
    public long FirstUnreadMessageId { get; }
    public bool IsPrivate { get; }
    public DateTimeOffset? LastMessageAt { get; }
    public bool HasPreview => !string.IsNullOrWhiteSpace(Preview);
    public bool HasUnreadMessages => UnreadCount > 0;
    public string Initials => string.Concat((FirstName + " " + LastName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(name => name[0])).ToUpperInvariant() is { Length: > 0 } initials
        ? initials
        : Title[..1].ToUpperInvariant();

    private static string CreatePreview(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elementStarts = StringInfo.ParseCombiningCharacters(normalized);
        return elementStarts.Length <= 20
            ? normalized
            : string.Concat(normalized.AsSpan(0, elementStarts[19]), "…");
    }
}

public sealed partial class MessageItemViewModel : ObservableObject
{
    private readonly long _currentUserId;

    public MessageItemViewModel(MessageModel message, bool isMine, IReadOnlyCollection<MessageAttachmentItemViewModel> attachments, long currentUserId)
    {
        Id = message.MessageId;
        Text = message.Text;
        IsMine = isMine;
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = attachments;
        MediaAttachments = attachments.Where(attachment => attachment.IsMedia).ToArray();
        FileAttachments = attachments.Where(attachment => attachment.IsFile).ToArray();
        _currentUserId = currentUserId;
        IsReadByCurrentUser = message.ReadBy.Contains(currentUserId);
        IsReadBySomeoneElse = isMine && message.ReadBy.Any(userId => userId != currentUserId);
    }

    public long Id { get; }
    public string Text { get; }
    public bool IsMine { get; }
    public DateTimeOffset SentAt { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> Attachments { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> MediaAttachments { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> FileAttachments { get; }
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasMedia => MediaAttachments.Count > 0;
    public bool HasFiles => FileAttachments.Count > 0;
    public bool HasOnlyMedia => !HasText && HasMedia && !HasFiles;
    public bool HasMetadataBelowMedia => !HasOnlyMedia;
    public bool IsSingleMedia => MediaAttachments.Count == 1;

    [ObservableProperty] private bool _isReadByCurrentUser;
    [ObservableProperty] private bool _isReadBySomeoneElse;

    public void MarkReadByCurrentUser() => IsReadByCurrentUser = true;

    public void RegisterReader(long userId)
    {
        if (userId == _currentUserId)
        {
            IsReadByCurrentUser = true;
        }
        else if (IsMine)
        {
            IsReadBySomeoneElse = true;
        }
    }
}

public sealed class MessageAttachmentItemViewModel(
    MessageAttachmentType type,
    string previewUrl,
    string fileUrl,
    string fileName,
    long size)
{
    public string PreviewUrl { get; } = previewUrl;
    public string FileUrl { get; } = fileUrl;
    public string FileName { get; } = string.IsNullOrWhiteSpace(fileName) ? "File" : fileName;
    public long Size { get; } = size;
    public bool IsVideo { get; } = type == MessageAttachmentType.Video;
    public bool IsMedia => type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif or MessageAttachmentType.Sticker;
    public bool IsFile => !IsMedia;
    public string SizeLabel => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024d:0.#} KB",
        _ => $"{Size / 1024d / 1024d:0.#} MB"
    };
}
