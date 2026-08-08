using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Presence;
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
    private readonly IOnlinePresenceService _presence;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly HashSet<long> _pendingReadMessageIds = [];
    private CancellationTokenSource? _readBatchCancellationTokenSource;
    private int _messageLoadVersion;
    private bool _isFeedAtBottom = true;
    // До первого подключения индикатор не показываем: у стримов ещё не было повода упасть.
    private bool _isRealtimeConnected = true;
    private bool _isPresenceConnected = true;

    public MessengerViewModel(
        IMessengerService messenger,
        IPrivateChatKeyStore privateChatKeyStore,
        IRealtimeMessengerService realtimeMessenger,
        IOnlinePresenceService presence,
        ILocalizationService localization,
        IUiDispatcher uiDispatcher,
        ProfileViewModel profile)
    {
        _messenger = messenger;
        _privateChatKeyStore = privateChatKeyStore;
        _realtimeMessenger = realtimeMessenger;
        _presence = presence;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        Profile = profile;
        _realtimeMessenger.MessageReceived += OnMessageReceived;
        _realtimeMessenger.MessageRead += OnMessageRead;
        _realtimeMessenger.PrivateMessageRead += OnPrivateMessageRead;
        _realtimeMessenger.ConnectionChanged += OnRealtimeConnectionChanged;
        _presence.PresenceChanged += OnPresenceChanged;
        _presence.ConnectionChanged += OnPresenceConnectionChanged;
    }

    public ObservableCollection<ChatItemViewModel> Chats { get; } = [];

    /// <summary>Отфильтрованная поиском проекция <see cref="Chats"/>; к ней привязан список в разметке.</summary>
    public ObservableCollection<ChatItemViewModel> VisibleChats { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    internal ILocalizationService Localization => _localization;

    public bool HasSelectedChat => SelectedChat is not null;

    public bool IsChatPlaceholderVisible => SelectedChat is null;

    /// <summary>Пока связь восстанавливается, статус собеседника устарел — шапка показывает вместо него индикатор.</summary>
    public bool IsPresenceLabelVisible => !IsReconnecting && SelectedChat?.HasPresence == true;

    public bool IsChatHeaderReconnectingVisible => IsReconnecting && HasSelectedChat;

    /// <summary>
    /// Дубль над списком чатов: шапка целиком скрыта, пока чат не выбран, и обрыв связи
    /// на пустом мессенджере был бы невидим. С заголовочным индикатором не совмещается.
    /// </summary>
    public bool IsChatListReconnectingVisible => IsReconnecting && !HasSelectedChat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedChat), nameof(IsChatPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsPresenceLabelVisible), nameof(IsChatHeaderReconnectingVisible), nameof(IsChatListReconnectingVisible))]
    private ChatItemViewModel? _selectedChat;
    [ObservableProperty] private string _draftText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MessageScrollRequest? _scrollRequest;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPresenceLabelVisible), nameof(IsChatHeaderReconnectingVisible), nameof(IsChatListReconnectingVisible))]
    private bool _isReconnecting;
    [ObservableProperty] private bool _isPrivateUnlockVisible;
    [ObservableProperty] private string _privatePassphrase = string.Empty;
    [ObservableProperty] private string? _privateUnlockError;
    [ObservableProperty] private bool _isProfileVisible;

    /// <summary>Профиль — оверлей поверх мессенджера, а не отдельная страница; открывается из шапки чата и нав-панели.</summary>
    public ProfileViewModel Profile { get; }

    public async Task LoadAsync()
    {
        var currentUserId = _messenger.CurrentUserId;
        if (currentUserId is null)
        {
            return;
        }

        IsLoading = true;
        ActionError = null;
        try
        {
            await _realtimeMessenger.StartAsync();
            var result = await _messenger.GetChatsAsync();
            if (!result.error.IsSuccess || result.chats is null)
            {
                ActionError = DescribeError(result.error);
                return;
            }

            var chats = result.chats.OrderByDescending(chat => chat.LastActivityAt?.ToDateTimeOffset());
            var items = await Task.WhenAll(chats.Select(chat => CreateChatItemAsync(chat, currentUserId.Value)));

            Chats.Clear();
            foreach (var item in items)
            {
                Chats.Add(item);
            }

            ApplyChatFilter();
            await TrackPresenceForChatsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Стирает всё, что относится к прошедшей сессии. Нужен при выходе из аккаунта: страница
    /// мессенджера закэширована, а сама ViewModel — синглтон, поэтому без сброса следующий
    /// вошедший увидел бы чужие чаты и открытую переписку. Полагаться на перезагрузку нельзя:
    /// она очищает список только при успешном ответе сервера и не трогает открытый чат.
    /// </summary>
    public void Reset()
    {
        CancelPendingReadBatch();
        _messageLoadVersion++;
        SelectedChat = null;
        Chats.Clear();
        VisibleChats.Clear();
        Messages.Clear();
        DraftText = string.Empty;
        SearchText = string.Empty;
        ScrollRequest = null;
        // Циклы остановлены вместе с сессией, о восстановлении связи сообщить уже некому:
        // без сброса «переподключение…» перешло бы в следующую сессию и залипло.
        _isRealtimeConnected = true;
        _isPresenceConnected = true;
        IsReconnecting = false;
        CancelPrivateUnlock();
    }

    partial void OnSearchTextChanged(string value) => ApplyChatFilter();

    /// <summary>
    /// Выбранный чат остаётся в выборке даже когда не подходит под фильтр: иначе список
    /// сбросил бы <c>SelectedItem</c> и открытая переписка закрылась бы на середине фразы.
    /// </summary>
    private void ApplyChatFilter()
    {
        var query = SearchText.Trim();
        var filtered = Chats.Where(chat =>
            query.Length == 0
            || chat == SelectedChat
            || chat.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        VisibleChats.Clear();
        foreach (var chat in filtered)
        {
            VisibleChats.Add(chat);
        }
    }

    partial void OnSelectedChatChanged(ChatItemViewModel? value)
    {
        _messageLoadVersion++;
        CancelPendingReadBatch();
        ResetMessageActionState();
        Messages.Clear();
        ScrollRequest = null;
        // Только что открытый чат следует за новыми сообщениями, пока пользователь не ушёл вверх.
        _isFeedAtBottom = true;
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

        ActionError = null;
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
                InsertMessageInOrder(CreatePrivateMessageItem(privateResult.message, currentUserId.Value));
                DraftText = string.Empty;
                RequestScroll(MessageScrollTarget.Bottom);
                return;
            }

            // Черновик остаётся в композере: отправлять заново пользователю нечего было бы.
            ActionError = DescribeError(privateResult.error);
            return;
        }

        var result = await _messenger.SendMessageAsync(SelectedChat.Id, DraftText.Trim(), ReplyTarget?.Id ?? 0);
        if (result.error.IsSuccess && result.message is not null)
        {
            InsertMessageInOrder(await CreateMessageItemAsync(result.message, currentUserId.Value));
            ApplyReplyQuoteState();
            DraftText = string.Empty;
            ReplyTarget = null;
            RequestScroll(MessageScrollTarget.Bottom);
            return;
        }

        ActionError = DescribeError(result.error);
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
        // Устаревание проверяется первым: об ошибке уже покинутого чата сообщать нельзя,
        // иначе баннер вылезал бы поверх открытого следующим.
        if (SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion)
        {
            return;
        }

        if (!result.error.IsSuccess || result.messages is null)
        {
            ActionError = DescribeError(result.error);
            return;
        }

        Messages.Clear();
        foreach (var message in result.messages.OrderBy(message => message.MessageId))
        {
            Messages.Add(await CreateMessageItemAsync(message, currentUserId.Value));
        }

        ApplyReplyQuoteState();
        if (hasUnreadMessages)
        {
            RequestScroll(MessageScrollTarget.Message, chat.FirstUnreadMessageId);
        }
        else
        {
            RequestScroll(MessageScrollTarget.Bottom);
        }

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

    [RelayCommand]
    private async Task OpenOwnProfileAsync()
    {
        await Profile.LoadOwnAsync();
        IsProfileVisible = true;
    }

    [RelayCommand]
    private async Task OpenPeerProfileAsync()
    {
        if (SelectedChat is not { PeerUserId: { } peerUserId } chat)
        {
            return;
        }

        await Profile.LoadForPeerAsync(peerUserId, chat.Id);
        IsProfileVisible = true;
    }

    [RelayCommand]
    private void CloseProfile() => IsProfileVisible = false;

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
        if (SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion)
        {
            return;
        }

        if (!result.error.IsSuccess || result.messages is null)
        {
            ActionError = DescribeError(result.error);
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

        if (hasUnreadMessages)
        {
            RequestScroll(MessageScrollTarget.Message, chat.FirstUnreadMessageId);
        }
        else
        {
            RequestScroll(MessageScrollTarget.Bottom);
        }
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
                if (!result.IsSuccess)
                {
                    ReportBackgroundError(result);
                }

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
        long? peerUserId = null;
        // Chat.Picture сервер отдаёт готовой ссылкой на файл, а не идентификатором,
        // поэтому её нельзя разрешать через Files — она уже готова к показу.
        var avatarUrl = chat.Picture;

        if (!chat.IsGroupChat)
        {
            var otherMember = chat.Members.FirstOrDefault(member => member.UserId != currentUserId);
            if (otherMember is not null)
            {
                peerUserId = otherMember.UserId;
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

        return new ChatItemViewModel(chat, title, firstName, lastName, avatarUrl, peerUserId, _localization);
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

    /// <summary>
    /// <see cref="MessageScrollRequest"/> — запись со сравнением по значению, поэтому повторная
    /// установка того же запроса не поднимет <c>PropertyChanged</c> и лента не прокрутится.
    /// Сброс в <c>null</c> гарантирует уведомление; поведение прокрутки на <c>null</c> не реагирует.
    /// </summary>
    private void RequestScroll(MessageScrollTarget target, long? messageId = null)
    {
        ScrollRequest = null;
        ScrollRequest = new MessageScrollRequest(target, messageId);
    }

    /// <summary>
    /// Лента отсортирована по возрастанию идентификатора. Сообщение из стрима может обогнать ответ
    /// на собственную отправку, поэтому позиция ищется, а уже показанный идентификатор молча
    /// пропускается: сервер рассылает новое сообщение и самому отправителю тоже.
    /// </summary>
    private bool InsertMessageInOrder(MessageItemViewModel message)
    {
        if (Messages.Any(item => item.Id == message.Id))
        {
            return false;
        }

        var index = Messages.Count;
        while (index > 0 && Messages[index - 1].Id > message.Id)
        {
            index--;
        }

        Messages.Insert(index, message);
        return true;
    }

    /// <summary>
    /// Лента сообщает, стоит ли она у нижней кромки. Догонять входящее прокруткой можно только
    /// тогда — иначе чтение истории сбрасывалось бы в конец при каждом чужом сообщении.
    /// </summary>
    [RelayCommand]
    private void FeedPositionChanged(bool isAtBottom) => _isFeedAtBottom = isAtBottom;

    private void OnMessageReceived(object? sender, IncomingMessage incoming) =>
        _uiDispatcher.Post(() => ApplyIncomingMessage(incoming));

    private void ApplyIncomingMessage(IncomingMessage incoming)
    {
        if (_messenger.CurrentUserId is not { } currentUserId)
        {
            return;
        }

        var chat = Chats.FirstOrDefault(item => item.Id == incoming.ChatId);
        if (chat is null)
        {
            _ = AppendUnknownChatAsync(incoming, currentUserId);
            return;
        }

        chat.ApplyIncomingMessage(
            incoming.Message.MessageId,
            incoming.Message.Text,
            incoming.Message.SentAt.ToDateTimeOffset(),
            countAsUnread: incoming.Message.SenderId != currentUserId);
        MoveChatToTop(chat);

        // Приватные чаты обслуживает отдельный шифрованный стрим, в этот их сообщения не попадают.
        if (SelectedChat?.Id != incoming.ChatId || SelectedChat.IsPrivate)
        {
            return;
        }

        _ = AppendIncomingMessageAsync(SelectedChat, incoming.Message, currentUserId, _messageLoadVersion);
    }

    private async Task AppendIncomingMessageAsync(ChatItemViewModel chat, MessageModel message, long currentUserId, int loadVersion)
    {
        // Ссылки на вложения разрешаются отдельными вызовами — за это время чат мог смениться.
        var item = await CreateMessageItemAsync(message, currentUserId);
        if (SelectedChat?.Id != chat.Id || loadVersion != _messageLoadVersion || !InsertMessageInOrder(item))
        {
            return;
        }

        ApplyReplyQuoteState();
        if (_isFeedAtBottom)
        {
            RequestScroll(MessageScrollTarget.Bottom);
        }
    }

    /// <summary>
    /// Чат мог появиться уже после загрузки списка. Подтягивается только он: <c>Clear</c> на
    /// привязанной коллекции обнулил бы <see cref="SelectedChat"/> и закрыл открытую переписку.
    /// </summary>
    private async Task AppendUnknownChatAsync(IncomingMessage incoming, long currentUserId)
    {
        var (error, chats) = await _messenger.GetChatsAsync();
        if (!error.IsSuccess)
        {
            ReportBackgroundError(error);
            return;
        }

        var definition = chats?.FirstOrDefault(item => item.Id == incoming.ChatId);
        if (definition is null || Chats.Any(item => item.Id == incoming.ChatId))
        {
            return;
        }

        Chats.Insert(0, await CreateChatItemAsync(definition, currentUserId));
        ApplyChatFilter();
        await TrackPresenceForChatsAsync();
    }

    /// <summary>
    /// Чат с новым сообщением поднимается наверх. <c>Move</c> вместо пересборки коллекции:
    /// он не пересоздаёт элементы и сохраняет выбор в списке.
    /// </summary>
    private void MoveChatToTop(ChatItemViewModel chat)
    {
        var index = Chats.IndexOf(chat);
        if (index > 0)
        {
            Chats.Move(index, 0);
        }

        var visibleIndex = VisibleChats.IndexOf(chat);
        if (visibleIndex > 0)
        {
            VisibleChats.Move(visibleIndex, 0);
        }
    }

    /// <summary>
    /// Онлайн-статус имеет смысл только для личных чатов: у группового нет одного собеседника.
    /// </summary>
    private Task TrackPresenceForChatsAsync() =>
        _presence.WatchAsync([.. Chats.Where(chat => chat.PeerUserId is not null).Select(chat => chat.PeerUserId!.Value)]);

    private void OnPresenceChanged(object? sender, UserPresence presence) =>
        _uiDispatcher.Post(() =>
        {
            foreach (var chat in Chats.Where(chat => chat.PeerUserId == presence.UserId))
            {
                chat.IsOnline = presence.IsOnline;
                chat.LastSeen = presence.LastSeen;
            }
        });

    private void OnRealtimeConnectionChanged(object? sender, bool isConnected) =>
        _uiDispatcher.Post(() =>
        {
            _isRealtimeConnected = isConnected;
            IsReconnecting = !_isRealtimeConnected || !_isPresenceConnected;
        });

    private void OnPresenceConnectionChanged(object? sender, bool isConnected) =>
        _uiDispatcher.Post(() =>
        {
            _isPresenceConnected = isConnected;
            IsReconnecting = !_isRealtimeConnected || !_isPresenceConnected;
        });

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
            ReportBackgroundError(result.error);
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
    private readonly ILocalizationService _localization;

    public ChatItemViewModel(Chat chat, string title, string firstName, string lastName, string avatarUrl, long? peerUserId, ILocalizationService localization)
    {
        _localization = localization;
        Definition = chat;
        Id = chat.Id;
        Title = title;
        FirstName = firstName;
        LastName = lastName;
        AvatarUrl = avatarUrl;
        PeerUserId = peerUserId;
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

    /// <summary>Собеседник личного чата; <c>null</c> у групповых — им присутствие не показывается.</summary>
    public long? PeerUserId { get; }

    // Превью и время последней активности переписывает входящее сообщение, поэтому они
    // наблюдаемые, а не вычисленные один раз в конструкторе.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _preview = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastMessageAtLabel))]
    private DateTimeOffset? _lastMessageAt;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadMessages))]
    private long _unreadCount;
    [ObservableProperty] private long _firstUnreadMessageId;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresenceLabel))]
    private bool _isOnline;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresenceLabel))]
    private DateTimeOffset? _lastSeen;
    public bool IsPrivate { get; }
    public bool IsPrivateAccepted => !IsPrivate || Definition.PrivateInviteState == PrivateChatInviteState.Accepted;
    public bool HasPresence => PeerUserId is not null;
    public string PresenceLabel => LastSeenFormatter.Format(_localization, IsOnline, LastSeen, DateTimeOffset.Now);

    /// <summary>В WinUI нет <c>StringFormat</c>, поэтому время приходит в разметку готовой строкой.</summary>
    public string LastMessageAtLabel => LastMessageAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;
    public bool HasPreview => !string.IsNullOrWhiteSpace(Preview);
    public bool HasUnreadMessages => UnreadCount > 0;
    public string Initials => string.Concat((FirstName + " " + LastName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(name => name[0])).ToUpperInvariant() is { Length: > 0 } initials
        ? initials
        : Title[..1].ToUpperInvariant();

    /// <summary>
    /// Превью приватного чата остаётся пустым: расшифровать текст здесь нечем,
    /// а показывать шифртекст нельзя.
    /// </summary>
    internal void ApplyIncomingMessage(long messageId, string text, DateTimeOffset sentAt, bool countAsUnread)
    {
        Preview = IsPrivate ? string.Empty : CreatePreview(text, 20);
        LastMessageAt = sentAt;
        if (!countAsUnread)
        {
            return;
        }

        UnreadCount++;
        if (FirstUnreadMessageId == 0)
        {
            FirstUnreadMessageId = messageId;
        }
    }

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
    /// <summary>В WinUI нет <c>StringFormat</c>, поэтому время приходит в разметку готовой строкой.</summary>
    public string SentAtLabel => SentAt.ToLocalTime().ToString("HH:mm");
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
    public bool IsVoice { get; } = type is MessageAttachmentType.Voice or MessageAttachmentType.Audio;
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
