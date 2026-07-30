using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.Globalization;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel : ObservableObject
{
    private readonly IMessengerService _messenger;
    private readonly IPrivateChatKeyStore _privateChatKeyStore;
    private readonly IRealtimeMessengerService _realtimeMessenger;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly HashSet<long> _pendingReadMessageIds = [];
    private CancellationTokenSource? _readBatchCancellationTokenSource;
    private int _messageLoadVersion;

    public MessengerViewModel(
        IMessengerService messenger,
        IPrivateChatKeyStore privateChatKeyStore,
        IRealtimeMessengerService realtimeMessenger,
        ILocalizationService localization,
        IUiDispatcher uiDispatcher)
    {
        _messenger = messenger;
        _privateChatKeyStore = privateChatKeyStore;
        _realtimeMessenger = realtimeMessenger;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _realtimeMessenger.MessageRead += OnMessageRead;
        _realtimeMessenger.PrivateMessageRead += OnPrivateMessageRead;
    }

    public ObservableCollection<ChatItemViewModel> Chats { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    internal ILocalizationService Localization => _localization;

    public bool HasSelectedChat => SelectedChat is not null;

    public bool IsChatPlaceholderVisible => SelectedChat is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedChat), nameof(IsChatPlaceholderVisible))]
    private ChatItemViewModel? _selectedChat;
    [ObservableProperty] private string _draftText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MessageScrollRequest? _scrollRequest;
    [ObservableProperty] private bool _isPrivateUnlockVisible;
    [ObservableProperty] private string _privatePassphrase = string.Empty;
    [ObservableProperty] private string? _privateUnlockError;

    public async Task LoadAsync()
    {
        var currentUserId = _messenger.CurrentUserId;
        if (currentUserId is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _realtimeMessenger.StartAsync();
            var result = await _messenger.GetChatsAsync();
            if (!result.error.IsSuccess || result.chats is null)
            {
                return;
            }

            var chats = result.chats.OrderByDescending(chat => chat.LastActivityAt?.ToDateTimeOffset());
            var items = await Task.WhenAll(chats.Select(chat => CreateChatItemAsync(chat, currentUserId.Value)));

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
        ResetMessageActionState();
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
        var currentUserId = _messenger.CurrentUserId;
        if (currentUserId is null || SelectedChat is null || string.IsNullOrWhiteSpace(DraftText))
        {
            return;
        }

        if (IsEditing)
        {
            await ApplyEditAsync();
            return;
        }

        if (SelectedChat.IsPrivate)
        {
            var key = await GetPrivateChatKeyAsync(SelectedChat);
            if (key is null)
            {
                ShowPrivateUnlock(SelectedChat);
                return;
            }

            var privateResult = await _messenger.SendPrivateMessageAsync(SelectedChat.Id, DraftText.Trim(), key);
            if (privateResult.error.IsSuccess && privateResult.message is not null)
            {
                Messages.Add(CreatePrivateMessageItem(privateResult.message, currentUserId.Value));
                DraftText = string.Empty;
                ScrollRequest = new MessageScrollRequest(MessageScrollTarget.Bottom);
            }
            return;
        }

        var result = await _messenger.SendMessageAsync(SelectedChat.Id, DraftText.Trim(), ReplyTarget?.Id ?? 0);
        if (result.error.IsSuccess && result.message is not null)
        {
            Messages.Add(await CreateMessageItemAsync(result.message, currentUserId.Value));
            ApplyReplyQuoteState();
            DraftText = string.Empty;
            ReplyTarget = null;
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
        var currentUserId = _messenger.CurrentUserId;
        if (currentUserId is null)
        {
            return;
        }

        if (chat.IsPrivate)
        {
            await LoadPrivateMessagesAsync(chat, loadVersion);
            return;
        }

        var hasUnreadMessages = chat.UnreadCount > 0 && chat.FirstUnreadMessageId > 0;
        var result = await _messenger.GetMessagesAsync(
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
            Messages.Add(await CreateMessageItemAsync(message, currentUserId.Value));
        }

        ApplyReplyQuoteState();
        ScrollRequest = hasUnreadMessages
            ? new MessageScrollRequest(MessageScrollTarget.Message, chat.FirstUnreadMessageId)
            : new MessageScrollRequest(MessageScrollTarget.Bottom);

        await LoadPinnedMessagesAsync(chat, loadVersion);
    }

    [RelayCommand]
    private async Task UnlockPrivateChatAsync()
    {
        var chat = SelectedChat;
        var currentUserId = _messenger.CurrentUserId;
        if (currentUserId is null || chat is null || !chat.IsPrivate || string.IsNullOrWhiteSpace(PrivatePassphrase))
        {
            return;
        }

        if (!chat.IsPrivateAccepted)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnavailable");
            return;
        }

        var key = _messenger.UnlockPrivateChat(chat.Definition, PrivatePassphrase);
        if (key is null)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnlockInvalid");
            return;
        }

        await _privateChatKeyStore.SaveAsync(_messenger.CurrentNodeAddress, currentUserId.Value, chat.Id, key);
        PrivatePassphrase = string.Empty;
        PrivateUnlockError = null;
        IsPrivateUnlockVisible = false;
        await LoadPrivateMessagesAsync(chat, _messageLoadVersion, key);
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
        int loadVersion,
        byte[]? knownKey = null)
    {
        if (!chat.IsPrivateAccepted)
        {
            PrivateUnlockError = _localization.GetString("Messenger_PrivateUnavailable");
            IsPrivateUnlockVisible = true;
            return;
        }

        var key = knownKey ?? await GetPrivateChatKeyAsync(chat);
        if (key is null)
        {
            ShowPrivateUnlock(chat);
            return;
        }

        var hasUnreadMessages = chat.UnreadCount > 0 && chat.FirstUnreadMessageId > 0;
        var result = await _messenger.GetPrivateMessagesAsync(
            chat.Id,
            key,
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
            var item = CreatePrivateMessageItem(message, _messenger.CurrentUserId!.Value);
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

    private async Task<byte[]?> GetPrivateChatKeyAsync(ChatItemViewModel chat)
    {
        var currentUserId = _messenger.CurrentUserId;
        return currentUserId is null
            ? null
            : await _privateChatKeyStore.TryGetAsync(_messenger.CurrentNodeAddress, currentUserId.Value, chat.Id);
    }

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

            var messageIds = _pendingReadMessageIds.ToArray();
            var result = chat.IsPrivate
                ? await _messenger.MarkPrivateMessagesReadAsync(chat.Id, messageIds.Max())
                : await _messenger.MarkMessagesReadAsync(messageIds);
            if (!result.IsSuccess || SelectedChat?.Id != chat.Id)
            {
                _pendingReadMessageIds.ExceptWith(messageIds);
                return;
            }

            var messagesToMark = (chat.IsPrivate
                ? Messages.Where(message => !message.IsMine && message.Id <= messageIds.Max())
                : Messages.Where(message => messageIds.Contains(message.Id)))
                .Where(message => !message.IsReadByCurrentUser)
                .ToArray();
            foreach (var message in messagesToMark)
            {
                message.MarkReadByCurrentUser();
            }

            _pendingReadMessageIds.ExceptWith(messageIds);
            UpdateUnreadState(chat, messagesToMark.Length);
            await RefreshUnreadStateAsync(chat);
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

    private async Task<ChatItemViewModel> CreateChatItemAsync(Chat chat, long currentUserId)
    {
        var title = string.IsNullOrWhiteSpace(chat.Title) ? _localization.GetString("Messenger_Chat") : chat.Title;
        var firstName = string.Empty;
        var lastName = string.Empty;
        // Chat.Picture сервер отдаёт готовой ссылкой на файл, а не идентификатором,
        // поэтому её нельзя разрешать через Files — она уже готова к показу.
        var avatarUrl = chat.Picture;

        if (!chat.IsGroupChat)
        {
            var otherMember = chat.Members.FirstOrDefault(member => member.UserId != currentUserId);
            if (otherMember is not null)
            {
                var userResult = await _messenger.GetUserDataAsync(otherMember.UserId);
                if (userResult.Error.IsSuccess && userResult.Data is not null)
                {
                    firstName = userResult.Data.FirstName;
                    lastName = userResult.Data.LastName;
                    title = string.Join(' ', new[] { firstName, lastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
                    title = string.IsNullOrWhiteSpace(title) ? _localization.GetString("Messenger_Chat") : title;
                    avatarUrl = userResult.Data.ProfilePicturePreviewUrl;
                }
            }
        }

        return new ChatItemViewModel(chat, title, firstName, lastName, avatarUrl);
    }

    private async Task<MessageItemViewModel> CreateMessageItemAsync(MessageModel message, long currentUserId)
    {
        // Пересланное сообщение приходит отдельным вложением — оно рисуется цитатой, а не файлом.
        var forwarded = message.Attachments
            .FirstOrDefault(attachment => attachment.Type == MessageAttachmentType.ForwardedMessage)?.ForwardedMessage;
        var attachments = await Task.WhenAll(message.Attachments
            .Where(attachment => attachment.Type != MessageAttachmentType.ForwardedMessage)
            .Select(CreateAttachmentItemAsync));

        return new MessageItemViewModel(this, message, message.SenderId == currentUserId, attachments, currentUserId, CreateForwardedContent(forwarded));
    }

    private ForwardedContentViewModel? CreateForwardedContent(ForwardedMessageModel? forwarded)
    {
        if (forwarded is null)
        {
            return null;
        }

        var preview = string.IsNullOrWhiteSpace(forwarded.Text)
            ? _localization.GetString("Messenger_Attachment")
            : forwarded.Text;
        return new ForwardedContentViewModel(forwarded.AuthorName, forwarded.OriginalMessageId, preview);
    }

    /// <summary>
    /// Сообщение с ссылкой на уже загруженный оригинал показывается цитатой ответа,
    /// иначе — блоком пересылки. Так же различает режимы веб-клиент.
    /// </summary>
    private void ApplyReplyQuoteState()
    {
        var loadedIds = Messages.Select(message => message.Id).ToHashSet();
        foreach (var message in Messages)
        {
            message.IsReplyQuote = message.Forwarded is not null && loadedIds.Contains(message.Forwarded.OriginalMessageId);
        }
    }

    private MessageItemViewModel CreatePrivateMessageItem(PrivateMessageModel message, long currentUserId)
    {
        var text = message.IsDeleted
            ? _localization.GetString("Messenger_PrivateDeleted")
            : message.DecryptionFailed
                ? _localization.GetString("Messenger_PrivateDecryptionFailed")
                : message.Text;
        return new MessageItemViewModel(this, message, text, message.SenderId == currentUserId, currentUserId);
    }

    private async Task<MessageAttachmentItemViewModel> CreateAttachmentItemAsync(AttachmentsModel attachment)
    {
        var previewUrl = attachment.PreviewUrl;
        if (attachment.Type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif or MessageAttachmentType.Sticker
            && string.IsNullOrWhiteSpace(previewUrl))
        {
            previewUrl = await ResolveFileUrlAsync(attachment.PreviewFileId);
        }

        var fileUrl = await ResolveFileUrlAsync(attachment.FileId);
        return new MessageAttachmentItemViewModel(
            attachment.Type,
            previewUrl,
            fileUrl,
            string.IsNullOrWhiteSpace(attachment.FileName) ? _localization.GetString("Messenger_File") : attachment.FileName,
            attachment.Size,
            attachment.ImageWidth,
            attachment.ImageHeight);
    }

    private async Task<string> ResolveFileUrlAsync(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return string.Empty;
        }

        return await _messenger.ResolveFileUrlAsync(fileId);
    }

    private void OnMessageRead(object? sender, MessageReadReceipt receipt)
    {
        _uiDispatcher.Post(() =>
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
        _uiDispatcher.Post(() =>
        {
            if (SelectedChat?.Id != receipt.ChatId || !SelectedChat.IsPrivate)
            {
                return;
            }

            var currentUserId = _messenger.CurrentUserId;
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

    private void UpdateUnreadState(ChatItemViewModel chat, int count)
    {
        chat.UnreadCount = Math.Max(0, chat.UnreadCount - Math.Max(0, count));
        var firstUnreadMessageId = Messages
            .Where(message => !message.IsMine && !message.IsReadByCurrentUser)
            .Select(message => message.Id)
            .DefaultIfEmpty()
            .Min();

        if (firstUnreadMessageId > 0 || chat.UnreadCount == 0)
        {
            chat.FirstUnreadMessageId = firstUnreadMessageId;
        }
    }

    private async Task RefreshUnreadStateAsync(ChatItemViewModel chat)
    {
        var result = await _messenger.GetChatsAsync();
        if (!result.error.IsSuccess || result.chats is null)
        {
            return;
        }

        var updatedChat = result.chats.FirstOrDefault(item => item.Id == chat.Id);
        if (updatedChat is null)
        {
            return;
        }

        chat.UnreadCount = updatedChat.CountUnread;
        chat.FirstUnreadMessageId = updatedChat.FirstUnreadMessageId;
    }
}

public sealed partial class ChatItemViewModel : ObservableObject
{
    public ChatItemViewModel(Chat chat, string title, string firstName, string lastName, string avatarUrl)
    {
        Definition = chat;
        Id = chat.Id;
        Title = title;
        FirstName = firstName;
        LastName = lastName;
        AvatarUrl = avatarUrl;
        IsPrivate = chat.ChatType == ChatType.Private;
        Preview = IsPrivate ? string.Empty : CreatePreview(chat.LastMessage?.Content?.Text ?? string.Empty, 20);
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

    /// <summary>
    /// Сжимает текст в одну строку и обрезает по числу видимых символов.
    /// </summary>
    internal static string CreatePreview(string text, int maxElements)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elementStarts = StringInfo.ParseCombiningCharacters(normalized);
        return elementStarts.Length <= maxElements
            ? normalized
            : normalized[..elementStarts[maxElements - 1]].TrimEnd() + "…";
    }
}

public sealed partial class MessageItemViewModel : ObservableObject
{
    private readonly long _currentUserId;

    public MessageItemViewModel(MessengerViewModel owner, MessageModel message, bool isMine, IReadOnlyCollection<MessageAttachmentItemViewModel> attachments, long currentUserId, ForwardedContentViewModel? forwarded)
    {
        Owner = owner;
        Forwarded = forwarded;
        Id = message.MessageId;
        Text = message.Text;
        IsMine = isMine;
        IsSystem = message.Type == MessageContentType.System;
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = attachments;
        MediaAttachments = attachments.Where(attachment => attachment.IsMedia).ToArray();
        FileAttachments = attachments.Where(attachment => attachment.IsFile).ToArray();
        _currentUserId = currentUserId;
        IsEdited = message.IsEdited;
        IsReadByCurrentUser = message.ReadBy.Contains(currentUserId);
        IsReadBySomeoneElse = isMine && message.ReadBy.Any(userId => userId != currentUserId);
    }

    public MessageItemViewModel(MessengerViewModel owner, PrivateMessageModel message, string text, bool isMine, long currentUserId)
    {
        Owner = owner;
        Id = message.MessageId;
        Text = text;
        IsMine = isMine;
        IsPrivateMessage = true;
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = Array.Empty<MessageAttachmentItemViewModel>();
        MediaAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        FileAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        _currentUserId = currentUserId;
    }

    public MessengerViewModel Owner { get; }
    public ForwardedContentViewModel? Forwarded { get; }
    public long Id { get; }
    public bool IsMine { get; }
    public bool IsSystem { get; }
    public bool IsPrivateMessage { get; }
    public DateTimeOffset SentAt { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> Attachments { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> MediaAttachments { get; }
    public IReadOnlyCollection<MessageAttachmentItemViewModel> FileAttachments { get; }
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasMedia => MediaAttachments.Count > 0;
    public bool HasFiles => FileAttachments.Count > 0;
    public bool HasOnlyMedia => !HasText && HasMedia && !HasFiles && !HasForwarded;
    public bool HasMetadataBelowMedia => !HasOnlyMedia;
    public bool IsSingleMedia => MediaAttachments.Count == 1;
    public int MediaColumns => IsSingleMedia ? 1 : 2;
    public double MediaPanelWidth => IsSingleMedia ? MediaAttachments.First().SingleMediaWidth : 344;
    public double MediaTileHeight => IsSingleMedia ? MediaAttachments.First().SingleMediaHeight : 128;

    // Приватные (E2E) и системные сообщения действий не поддерживают — как в веб-версии.
    public bool CanUseActions => !IsSystem && !IsPrivateMessage;
    public bool CanModify => CanUseActions && IsMine;
    public bool CanCopyText => CanUseActions && HasText;
    public bool CanCopyImage => CanUseActions && IsSingleMedia && MediaAttachments.First().IsImageOrGif;
    public string PinMenuHeader => Owner.Localization.GetString(IsPinned ? "Messenger_Unpin" : "Messenger_Pin");
    public bool HasForwarded => Forwarded is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(CanCopyText))]
    private string _text = string.Empty;
    [ObservableProperty] private bool _isReplyQuote;
    [ObservableProperty] private bool _isEdited;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinMenuHeader))]
    private bool _isPinned;
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

/// <summary>
/// Содержимое пересланного сообщения. Используется и для цитаты ответа, и для блока пересылки.
/// </summary>
public sealed class ForwardedContentViewModel(string authorName, long originalMessageId, string preview)
{
    public string AuthorName { get; } = authorName;
    public long OriginalMessageId { get; } = originalMessageId;
    public string Preview { get; } = preview;
}

public sealed class MessageAttachmentItemViewModel(
    MessageAttachmentType type,
    string previewUrl,
    string fileUrl,
    string fileName,
    long size,
    int imageWidth,
    int imageHeight)
{
    public string PreviewUrl { get; } = previewUrl;
    public string FileUrl { get; } = fileUrl;
    public string FileName { get; } = fileName;
    public long Size { get; } = size;
    public int ImageWidth { get; } = imageWidth;
    public int ImageHeight { get; } = imageHeight;
    public bool IsVideo { get; } = type == MessageAttachmentType.Video;
    public bool IsImageOrGif { get; } = type is MessageAttachmentType.Image or MessageAttachmentType.Gif;
    public bool IsMedia => type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif or MessageAttachmentType.Sticker;
    public bool IsFile => !IsMedia;
    public string FileTypeLabel => type.ToString();
    public double SingleMediaWidth => GetSingleMediaSize().Width;
    public double SingleMediaHeight => GetSingleMediaSize().Height;
    public string SizeLabel => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024d:0.#} KB",
        _ => $"{Size / 1024d / 1024d:0.#} MB"
    };

    private (double Width, double Height) GetSingleMediaSize()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0)
        {
            return (344, 258);
        }

        var scale = Math.Min(344d / ImageWidth, 258d / ImageHeight);
        return (Math.Round(ImageWidth * scale), Math.Round(ImageHeight * scale));
    }
}
