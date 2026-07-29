using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class MessengerViewModel : ObservableObject
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private readonly IPrivateChatKeyStore _privateChatKeyStore;
    private readonly IRealtimeMessengerService _realtimeMessenger;
    private readonly ILocalizationService _localization;
    private readonly HashSet<long> _pendingReadMessageIds = [];
    private CancellationTokenSource? _readBatchCancellationTokenSource;
    private int _messageLoadVersion;

    public MessengerViewModel(
        WebApiClient webApi,
        IClientSession session,
        IPrivateChatKeyStore privateChatKeyStore,
        IRealtimeMessengerService realtimeMessenger,
        ILocalizationService localization)
    {
        _webApi = webApi;
        _session = session;
        _privateChatKeyStore = privateChatKeyStore;
        _realtimeMessenger = realtimeMessenger;
        _localization = localization;
        _realtimeMessenger.MessageRead += OnMessageRead;
        _realtimeMessenger.PrivateMessageRead += OnPrivateMessageRead;
    }

    public ObservableCollection<ChatItemViewModel> Chats { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    [ObservableProperty] private ChatItemViewModel? _selectedChat;
    [ObservableProperty] private string _draftText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MessageScrollRequest? _scrollRequest;
    [ObservableProperty] private bool _isPrivateUnlockVisible;
    [ObservableProperty] private string _privatePassphrase = string.Empty;
    [ObservableProperty] private string? _privateUnlockError;

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
            await _realtimeMessenger.StartAsync(parameters);
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
        _messageLoadVersion++;
        CancelPendingReadBatch();
        Messages.Clear();
        ScrollRequest = null;
        IsPrivateUnlockVisible = false;
        PrivateUnlockError = null;
        PrivatePassphrase = string.Empty;
        if (value is not null)
        {
            _ = LoadMessagesAsync(value, _messageLoadVersion);
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

        if (SelectedChat.IsPrivate)
        {
            var key = await GetPrivateChatKeyAsync(SelectedChat, parameters);
            if (key is null)
            {
                ShowPrivateUnlock(SelectedChat);
                return;
            }

            var privateResult = await _webApi.SendPrivateMessage(SelectedChat.Id, DraftText.Trim(), key, parameters);
            if (privateResult.error.IsSuccess && privateResult.message is not null)
            {
                Messages.Add(CreatePrivateMessageItem(privateResult.message, parameters.UserId));
                DraftText = string.Empty;
                ScrollRequest = new MessageScrollRequest(MessageScrollTarget.Bottom);
            }
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
            ScrollRequest = new MessageScrollRequest(MessageScrollTarget.Bottom);
        }
    }

    [RelayCommand]
    private void MessageBecameVisible(MessageItemViewModel message)
    {
        var chat = SelectedChat;
        if (chat is null || message.IsMine || message.IsReadByCurrentUser)
        {
            return;
        }

        if (_pendingReadMessageIds.Add(message.Id))
        {
            ScheduleReadBatch(chat);
        }
    }

    private async Task LoadMessagesAsync(ChatItemViewModel chat, int loadVersion)
    {
        var parameters = _session.CurrentConnection?.ConnectionParameters;
        if (parameters is null)
        {
            return;
        }

        if (chat.IsPrivate)
        {
            await LoadPrivateMessagesAsync(chat, parameters, loadVersion);
            return;
        }

        var hasUnreadMessages = chat.UnreadCount > 0 && chat.FirstUnreadMessageId > 0;
        var result = await _webApi.GetMessagesWithOffset(
            parameters,
            chat.Id,
            hasUnreadMessages ? chat.FirstUnreadMessageId : 0,
            50,
            hasUnreadMessages ? 50 : 0);
        if (!result.error.IsSuccess || result.messages is null || SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion)
        {
            return;
        }

        Messages.Clear();
        foreach (var message in result.messages.OrderBy(message => message.MessageId))
        {
            Messages.Add(await CreateMessageItemAsync(message, parameters.UserId, parameters));
        }

        ScrollRequest = hasUnreadMessages
            ? new MessageScrollRequest(MessageScrollTarget.Message, chat.FirstUnreadMessageId)
            : new MessageScrollRequest(MessageScrollTarget.Bottom);
    }

    [RelayCommand]
    private async Task UnlockPrivateChatAsync()
    {
        var parameters = _session.CurrentConnection?.ConnectionParameters;
        var chat = SelectedChat;
        if (parameters is null || chat is null || !chat.IsPrivate || string.IsNullOrWhiteSpace(PrivatePassphrase))
        {
            return;
        }

        if (!chat.IsPrivateAccepted)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnavailable");
            return;
        }

        var key = WebApiClient.UnlockPrivateChat(chat.Definition, PrivatePassphrase);
        if (key is null)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnlockInvalid");
            return;
        }

        await _privateChatKeyStore.SaveAsync(GetNodeAddress(), parameters.UserId, chat.Id, key);
        PrivatePassphrase = string.Empty;
        PrivateUnlockError = null;
        IsPrivateUnlockVisible = false;
        await LoadPrivateMessagesAsync(chat, parameters, _messageLoadVersion, key);
    }

    [RelayCommand]
    private void CancelPrivateUnlock()
    {
        PrivatePassphrase = string.Empty;
        PrivateUnlockError = null;
        IsPrivateUnlockVisible = false;
    }

    private async Task LoadPrivateMessagesAsync(
        ChatItemViewModel chat,
        GlobalParam parameters,
        int loadVersion,
        byte[]? knownKey = null)
    {
        if (!chat.IsPrivateAccepted)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnavailable");
            IsPrivateUnlockVisible = true;
            return;
        }

        var key = knownKey ?? await GetPrivateChatKeyAsync(chat, parameters);
        if (key is null)
        {
            ShowPrivateUnlock(chat);
            return;
        }

        var hasUnreadMessages = chat.UnreadCount > 0 && chat.FirstUnreadMessageId > 0;
        var result = await _webApi.ListPrivateMessages(
            chat.Id,
            key,
            parameters,
            hasUnreadMessages ? chat.FirstUnreadMessageId : 0,
            50,
            hasUnreadMessages ? 50 : 0);
        if (!result.error.IsSuccess || result.messages is null || SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion)
        {
            return;
        }

        Messages.Clear();
        foreach (var message in result.messages.OrderBy(message => message.MessageId))
        {
            var item = CreatePrivateMessageItem(message, parameters.UserId);
            if (!hasUnreadMessages || item.Id < chat.FirstUnreadMessageId)
            {
                item.MarkReadByCurrentUser();
            }

            Messages.Add(item);
        }

        ScrollRequest = hasUnreadMessages
            ? new MessageScrollRequest(MessageScrollTarget.Message, chat.FirstUnreadMessageId)
            : new MessageScrollRequest(MessageScrollTarget.Bottom);
    }

    private async Task<byte[]?> GetPrivateChatKeyAsync(ChatItemViewModel chat, GlobalParam parameters) =>
        await _privateChatKeyStore.TryGetAsync(GetNodeAddress(), parameters.UserId, chat.Id);

    private void ShowPrivateUnlock(ChatItemViewModel chat)
    {
        if (SelectedChat?.Id != chat.Id)
        {
            return;
        }

        PrivatePassphrase = string.Empty;
        PrivateUnlockError = null;
        IsPrivateUnlockVisible = true;
    }

    private string GetNodeAddress() => _session.CurrentConnection?.Profile.BeaconAddress ?? string.Empty;

    private void ScheduleReadBatch(ChatItemViewModel chat)
    {
        _readBatchCancellationTokenSource?.Cancel();
        _readBatchCancellationTokenSource?.Dispose();
        _readBatchCancellationTokenSource = new CancellationTokenSource();
        _ = FlushReadBatchAsync(chat, _readBatchCancellationTokenSource.Token);
    }

    private async Task FlushReadBatchAsync(ChatItemViewModel chat, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            if (SelectedChat?.Id != chat.Id || _pendingReadMessageIds.Count == 0)
            {
                return;
            }

            var parameters = _session.CurrentConnection?.ConnectionParameters;
            if (parameters is null)
            {
                return;
            }

            var messageIds = _pendingReadMessageIds.ToArray();
            var result = chat.IsPrivate
                ? await _webApi.MarkPrivateMessagesAsRead(chat.Id, messageIds.Max(), parameters)
                : await _webApi.MarkMessageAsRead(parameters, messageIds.ToList());
            if (!result.IsSuccess || SelectedChat?.Id != chat.Id)
            {
                _pendingReadMessageIds.ExceptWith(messageIds);
                return;
            }

            foreach (var message in chat.IsPrivate
                ? Messages.Where(message => !message.IsMine && message.Id <= messageIds.Max())
                : Messages.Where(message => messageIds.Contains(message.Id)))
            {
                message.MarkReadByCurrentUser();
            }

            _pendingReadMessageIds.ExceptWith(messageIds);
            chat.UnreadCount = Math.Max(0, chat.UnreadCount - messageIds.Length);
            chat.FirstUnreadMessageId = Messages
                .Where(message => !message.IsMine && !message.IsReadByCurrentUser)
                .Select(message => message.Id)
                .DefaultIfEmpty()
                .Min();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingReadBatch()
    {
        _readBatchCancellationTokenSource?.Cancel();
        _readBatchCancellationTokenSource?.Dispose();
        _readBatchCancellationTokenSource = null;
        _pendingReadMessageIds.Clear();
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

    private MessageItemViewModel CreatePrivateMessageItem(PrivateMessageModel message, long currentUserId)
    {
        var text = message.IsDeleted
            ? _localization.GetString("Messenger_PrivateDeleted")
            : message.DecryptionFailed
                ? _localization.GetString("Messenger_PrivateDecryptionFailed")
                : message.Text;
        return new MessageItemViewModel(message, text, message.SenderId == currentUserId, currentUserId);
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

    private void OnMessageRead(object? sender, MessageReadReceipt receipt)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (SelectedChat?.Id != receipt.ChatId || SelectedChat.IsPrivate)
            {
                return;
            }

            var message = Messages.FirstOrDefault(item => item.Id == receipt.MessageId);
            if (message is null)
            {
                return;
            }

            var wasUnread = !message.IsReadByCurrentUser;
            foreach (var userId in receipt.ReadBy)
            {
                message.RegisterReader(userId);
            }

            if (wasUnread && message.IsReadByCurrentUser)
            {
                UpdateUnreadState(SelectedChat, 1);
            }
        });
    }

    private void OnPrivateMessageRead(object? sender, PrivateMessageReadReceipt receipt)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (SelectedChat?.Id != receipt.ChatId || !SelectedChat.IsPrivate)
            {
                return;
            }

            var currentUserId = _session.CurrentConnection?.ConnectionParameters.UserId;
            if (currentUserId is null)
            {
                return;
            }

            if (receipt.UserId == currentUserId)
            {
                var newlyRead = Messages.Where(message => !message.IsMine && message.Id <= receipt.LastReadMessageId && !message.IsReadByCurrentUser).ToArray();
                foreach (var message in newlyRead)
                {
                    message.MarkReadByCurrentUser();
                }

                UpdateUnreadState(SelectedChat, newlyRead.Length);
                return;
            }

            foreach (var message in Messages.Where(message => message.IsMine && message.Id <= receipt.LastReadMessageId))
            {
                message.RegisterReader(receipt.UserId);
            }
        });
    }

    private static void UpdateUnreadState(ChatItemViewModel chat, int count)
    {
        if (count <= 0)
        {
            return;
        }

        chat.UnreadCount = Math.Max(0, chat.UnreadCount - count);
    }
}

public sealed partial class ChatItemViewModel : ObservableObject
{
    public ChatItemViewModel(Chat chat, string title, string firstName, string lastName, string avatarUrl)
    {
        Definition = chat;
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
    internal Chat Definition { get; }
    public string Title { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string AvatarUrl { get; }
    public string Preview { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadMessages))]
    private long _unreadCount;
    [ObservableProperty] private long _firstUnreadMessageId;
    public bool IsPrivate { get; }
    public bool IsPrivateAccepted => !IsPrivate || Definition.PrivateInviteState == PrivateChatInviteState.Accepted;
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
            : normalized[..elementStarts[19]].TrimEnd() + "…";
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

    public MessageItemViewModel(PrivateMessageModel message, string text, bool isMine, long currentUserId)
    {
        Id = message.MessageId;
        Text = text;
        IsMine = isMine;
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = Array.Empty<MessageAttachmentItemViewModel>();
        MediaAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        FileAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        _currentUserId = currentUserId;
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
