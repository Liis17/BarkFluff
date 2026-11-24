using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Presence;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Markdown;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
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
    private readonly ISettingsPreferences? _stickerPreferences;
    private readonly HashSet<long> _pendingReadMessageIds = [];
    private CancellationTokenSource? _readBatchCancellationTokenSource;
    private int _chatVersion;
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
        ProfileViewModel profile,
        OutgoingMessageQueue outgoing,
        TimeProvider? timeProvider = null,
        IChatFoldersState? chatFolders = null,
        IChatTypingService? typing = null,
        MessageNotificationService? notifications = null,
        SettingsNotificationsViewModel? notificationSettings = null,
        StickerPickerViewModel? stickerPicker = null,
        ISettingsPreferences? stickerPreferences = null,
        StickerFrameCache? stickerFrames = null,
        IVoiceRecorder? voiceRecorder = null,
        IVoicePlayer? voicePlayer = null,
        IChatDraftStore? draftStore = null,
        IChatDraftRemote? draftRemote = null,
        ChatListCache? chatCache = null,
        IAuthenticationService? authentication = null,
        IOnboardingNavigationService? navigation = null)
    {
        _messenger = messenger;
        _chatCache = chatCache;
        _authentication = authentication;
        _navigation = navigation;
        _draftStore = draftStore;
        _draftRemote = draftRemote;
        _outgoing = outgoing;
        _privateChatKeyStore = privateChatKeyStore;
        _realtimeMessenger = realtimeMessenger;
        _presence = presence;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        AttachmentComposer = new(localization);
        var clock = timeProvider ?? TimeProvider.System;
        Voice = new(voiceRecorder, localization, uiDispatcher, clock, VoiceComposer.TemporaryDirectory);
        _voicePlayback = new(voicePlayer, uiDispatcher, localization, clock);
        Voice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VoiceComposer.IsBusy))
                NotifyVoiceComposerChanged();
        };
        AttachmentComposer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AttachmentComposer.HasFiles))
                NotifyVoiceComposerChanged();
        };
        SendCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SendCommand.IsRunning))
                NotifyVoiceComposerChanged();
        };
        StickerPicker = stickerPicker;
        _stickerPreferences = stickerPreferences;
        _stickerFrames = stickerFrames;
        _muteClock = timeProvider ?? TimeProvider.System;
        _chatFolders = chatFolders;
        _typing = typing;
        _notifications = notifications;
        _notificationSettings = notificationSettings;
        Profile = profile;
        Profile.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProfileViewModel.SelectedAttachmentTabIndex))
                _voicePlayback.StopProfilePlayback();
        };
        _presence.PresenceChanged += OnPresenceChanged;
        _presence.ConnectionChanged += OnPresenceConnectionChanged;
    }

    public ObservableCollection<ChatItemViewModel> Chats { get; } = [];

    /// <summary>Отфильтрованная поиском проекция <see cref="Chats"/>; к ней привязан список в разметке.</summary>
    public ObservableCollection<ChatItemViewModel> VisibleChats { get; } = [];
    public ObservableCollection<UserSearchResultViewModel> SearchResults { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    internal ILocalizationService Localization => _localization;

    public bool HasSelectedChat => SelectedChat is not null;

    public bool IsChatPlaceholderVisible => SelectedChat is null;

    /// <summary>Пока связь восстанавливается, статус собеседника устарел — шапка показывает вместо него индикатор.</summary>
    public bool IsPresenceLabelVisible => !IsReconnecting && TypingLabel.Length == 0 && SelectedChat?.HasPresence == true;

    public bool IsChatHeaderReconnectingVisible => IsReconnecting && HasSelectedChat;

    /// <summary>
    /// Дубль над списком чатов: шапка целиком скрыта, пока чат не выбран, и обрыв связи
    /// на пустом мессенджере был бы невидим. С заголовочным индикатором не совмещается.
    /// </summary>
    public bool IsChatListReconnectingVisible => IsReconnecting && !HasSelectedChat && !IsOffline;

    public bool IsSearchResultsVisible => SearchResults.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedChat), nameof(IsChatPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsPresenceLabelVisible), nameof(IsChatHeaderReconnectingVisible), nameof(IsChatListReconnectingVisible))]
    private ChatItemViewModel? _selectedChat;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowVoiceMicrophone), nameof(ShowComposerSend))]
    private string _draftText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MessageScrollRequest? _scrollRequest;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPresenceLabelVisible), nameof(IsChatHeaderReconnectingVisible), nameof(IsChatListReconnectingVisible))]
    [NotifyPropertyChangedFor(nameof(IsTypingLabelVisible))]
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

        if (_realtimeSession is not null && !IsCurrent(_realtimeSession))
        {
            Reset();
        }

        ActionError = null;
        if (_realtimeSession is null)
            IsOffline = _authentication?.IsRestoredSessionOffline == true;
        StartRealtimeSession(currentUserId.Value);
        var session = _realtimeSession!;
        var token = session.Lifetime.Token;
        if (session.Loading)
            return;
        session.Loading = true;
        IsLoading = true;
        try
        {
            var cachedFolders = await LoadChatCacheAsync(session);
            if (!IsCurrent(session))
                return;
            _chatFolders?.Start(cachedFolders, IsOffline);
            if (IsOffline)
            {
                IsLoading = false;
                if (!await ReconnectRestoredSessionAsync(session, token) || !IsCurrent(session))
                    return;
                _ = _chatFolders?.RefreshAsync();
                if (SelectedChat is { } selected)
                {
                    WatchTyping(selected);
                    _ = LoadMessagesAsync(selected, _chatVersion);
                }
            }

            await _realtimeMessenger.StartAsync(token);
            if (IsCurrent(session))
            {
                await RefreshChatsAsync(session, reportError: true, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            session.Loading = false;
            if (IsCurrent(session))
            {
                IsLoading = false;
            }
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
        if (SelectedChat is null)
            _ = CancelAttachmentsAsync();
        ResetTyping();
        EndRealtimeSession();
        ActionError = null;
        IsLoading = false;
        CancelPendingReadBatch();
        _chatVersion++;
        SelectedChat = null;
        IsProfileVisible = false;
        Profile.Reset();
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
        IsOffline = false;
        CancelPrivateUnlock();
    }

    /// <summary>
    /// Выбранный чат остаётся в выборке даже когда не подходит под фильтр: иначе список
    /// сбросил бы <c>SelectedItem</c> и открытая переписка закрылась бы на середине фразы.
    /// </summary>
    private void ApplyChatFilter()
    {
        var query = SearchText.Trim();
        UpdateChatSearchResults(query);
        var filtered = Chats.Where(chat =>
            query.Length == 0
            || chat == SelectedChat
            || MatchesChat(chat, query)).ToArray();

        var wanted = filtered.ToHashSet();
        for (var index = VisibleChats.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(VisibleChats[index]))
            {
                VisibleChats.RemoveAt(index);
            }
        }

        for (var index = 0; index < filtered.Length; index++)
        {
            var item = filtered[index];
            if (index < VisibleChats.Count && VisibleChats[index] == item)
            {
                continue;
            }

            // WinUI 2.5 превращает Move в Reset. Удаляем и вставляем только соседей
            // выбранного элемента, чтобы привязка SelectedItem не потеряла выбор.
            if (item == SelectedChat && VisibleChats.Contains(item))
            {
                while (VisibleChats[index] != item)
                {
                    VisibleChats.RemoveAt(index);
                }
                continue;
            }

            VisibleChats.Remove(item);
            VisibleChats.Insert(index, item);
        }
    }

    partial void OnSelectedChatChanged(ChatItemViewModel? oldValue, ChatItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            _realtimeSession?.Journal.LeaveChat(oldValue.Id, oldValue.IsPrivate);
        }

        _feedLoaded = false;
        _feedIncludesTail = true;
        _chatVersion++;
        RefreshOutgoing();
        StickerPicker?.Close();
        if (!IsOffline)
            WatchTyping(newValue);
        _ = CancelAttachmentsAsync();
        NotifyVoiceComposerChanged();
        OnPropertyChanged(nameof(HasPrivateAttachmentRestriction));
        ResetHistoryWindow();
        _feedLoadCompletion?.TrySetResult();
        _feedLoadCompletion = null;
        _reconnectFeed?.TrySetResult();
        _reconnectFeed = null;
        CancelPendingReadBatch();
        ChangeComposerChat(oldValue, newValue);
        ScrollRequest = null;
        // Только что открытый чат следует за новыми сообщениями, пока пользователь не ушёл вверх.
        _isFeedAtBottom = true;
        IsPrivateUnlockVisible = false;
        PrivateUnlockError = null;
        PrivatePassphrase = string.Empty;
        if (newValue is not null)
        {
            ClearChatNotifications(newValue);
            if (!IsOffline)
                _ = LoadMessagesAsync(newValue, _chatVersion);
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var currentUserId = _messenger.CurrentUserId;
        if (Voice.IsBusy || currentUserId is null || SelectedChat is null || !SelectedChat.CanSend ||
            (string.IsNullOrWhiteSpace(DraftText) && (!CanAttach || !AttachmentComposer.HasFiles)))
        {
            return;
        }

        if (IsEditing)
        {
            await ApplyEditAsync();
            return;
        }

        var chat = SelectedChat;
        var session = _realtimeSession;
        var version = _chatVersion;
        if (!IsOpen(session, chat, version))
        {
            return;
        }

        _typing?.StopSending();
        var draft = DraftText;
        var reply = ReplyTarget;
        var draftGeneration = _drafts?.Generation(chat.Id);
        var sentText = AttachmentComposer.HasVoice ? string.Empty : draft.Trim();
        using var reading = session!.Journal.ReadHistory(chat.Id, chat.IsPrivate);
        var revision = session.Journal.Revision;
        ActionError = null;
        if (!chat.IsPrivate)
        {
            var composer = AttachmentComposer;
            var files = composer.Files.Select(file => (file.Source, file.UploadType)).ToArray();
            var token = composer.ScopeToken;
            composer.IsSending = true;
            try
            {
                if (!await EnqueueOutgoingAsync(session, chat.Id, sentText, reply?.Id ?? 0, files: files, token: token))
                    return;
                ClearSentDraft();
                if (IsOpen(session, chat, version))
                    _ = composer.ClearAsync();
            }
            finally
            {
                if (IsOpen(session, chat, version))
                    composer.IsSending = false;
            }
        }
        else
        {
            var key = await GetPrivateChatKeyAsync(chat);
            if (!IsOpen(session, chat, version))
            {
                return;
            }

            if (key is null)
            {
                ShowPrivateUnlock(chat);
                return;
            }

            var result = await _messenger.SendPrivateMessageAsync(chat.Id, draft.Trim(), key);
            if (result.error is { IsSuccess: true } && result.message is not null)
                ClearSentDraft();
            if (!IsOpen(session, chat, version))
            {
                return;
            }

            if (!result.error.IsSuccess || result.message is not { } message)
            {
                ActionError = DescribeError(result.error);
                return;
            }

            if (_feedIncludesTail)
            {
                await MergeSentRowAsync(session, chat, version,
                    new(message.MessageId, message.SentAt.ToDateTimeOffset(), null, message), revision);
            }
        }

        if (IsOpen(session, chat, version))
        {
            if (_feedIncludesTail)
            {
                RequestScroll(MessageScrollTarget.Bottom);
            }
            else
            {
                await ReloadHistoryTailAsync();
            }
        }

        void ClearSentDraft()
        {
            if (!IsCurrent(session) || sentText != draft.Trim())
                return;

            if (draftGeneration is { } generation)
                _drafts?.ClearSent(chat.Id, generation, sentText, reply?.Id ?? 0);
            else if (IsOpen(session, chat, version) && !IsEditing && DraftText == draft && ReplyTarget == reply)
                SetComposerDraft("");
        }
    }

    [RelayCommand]
    private void MessageBecameVisible(MessageItemViewModel message)
    {
        var chat = SelectedChat;
        if (!_feedLoaded || IsHistoryUpdating || chat is null || message.IsMine || message.IsReadByCurrentUser || !Messages.Contains(message))
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
        if (chat.IsPrivate)
        {
            await LoadPrivateMessagesAsync(chat, loadVersion);
            return;
        }

        await LoadFeedSnapshotAsync(chat, loadVersion, null);
    }

    private async Task LoadFeedSnapshotAsync(ChatItemViewModel chat, int loadVersion, byte[]? key)
    {
        var session = _realtimeSession;
        if (!IsOpen(session, chat, loadVersion))
        {
            return;
        }

        var window = _historyWindowLifetime.Token;
        bool IsCurrentWindow() => IsOpenWindow(session, chat, loadVersion, window);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _feedLoadCompletion = completion;
        try
        {
            using var reading = session!.Journal.ReadHistory(chat.Id, chat.IsPrivate);
            var revision = session.Journal.Revision;
            var firstUnread = chat.UnreadCount > 0 ? chat.FirstUnreadMessageId : 0;
            _feedIncludesTail = firstUnread == 0;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token, window);
            var token = lifetime.Token;
            var (error, rows) = await GetHistoryPageAsync(chat, key, firstUnread, SnapshotPageSize,
                firstUnread > 0 ? SnapshotPageSize : 0, token);
            if (!IsCurrentWindow())
            {
                return;
            }

            if (firstUnread > 0 && IsMissingAnchor(error))
            {
                (error, rows) = await GetHistoryPageAsync(chat, key, 0, SnapshotPageSize, 0, token);
                if (!IsCurrentWindow())
                {
                    return;
                }
                firstUnread = 0;
            }

            // Устаревший запрос мог завершиться ошибкой после смены чата.
            if (!error.IsSuccess && error.ErrorCode != 1)
            {
                ActionError = DescribeError(error);
                return;
            }

            await PrepareHistoryAsync(session, chat, rows, revision, IsCurrentWindow, token, prepared =>
            {
                IsHistoryUpdating = true;
                try
                {
                    SetHistoryEdges(rows, firstUnread);
                    ApplyHistoryRows(session, chat, prepared, id => chat.IsPrivate && (firstUnread == 0 || id < firstUnread));
                    CompleteFeedLoad(firstUnread);
                }
                finally
                {
                    IsHistoryUpdating = false;
                }
            }, key, HistoryRange(rows, firstUnread));

            if (IsCurrentWindow() && !chat.IsPrivate)
            {
                await RefreshPinsAsync(chat, loadVersion, reportError: true);
            }
        }
        catch (OperationCanceledException) when (!IsCurrentWindow())
        {
        }
        finally
        {
            if (ReferenceEquals(_feedLoadCompletion, completion))
            {
                _feedLoadCompletion = null;
            }
            completion.TrySetResult();
        }
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

        var session = _realtimeSession;
        var version = _chatVersion;
        await _privateChatKeyStore.SaveAsync(_messenger.CurrentNodeAddress, currentUserId.Value, chat.Id, key);
        if (!IsOpen(session, chat, version))
        {
            return;
        }

        PrivatePassphrase = string.Empty;
        PrivateUnlockError = null;
        IsPrivateUnlockVisible = false;
        await LoadPrivateMessagesAsync(chat, version, key);
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

    public async Task OpenSearchResultAsync(UserSearchResultViewModel result)
    {
        var session = _realtimeSession;
        if (session is null || !IsCurrent(session))
        {
            return;
        }

        var existing = Chats.FirstOrDefault(chat => chat.PeerUserId == result.UserId);
        if (existing is not null)
        {
            ResetSearch();
            SelectedChat = existing;
            return;
        }

        ActionError = null;
        var token = session.Lifetime.Token;
        (ErrorReturner error, string chatId) chatIdResult;
        try
        {
            chatIdResult = await _messenger.GetPersonChatIdAsync(result.UserId, token).WaitAsync(token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }
        if (!IsCurrent(session))
        {
            return;
        }
        if (chatIdResult.error is not { IsSuccess: true })
        {
            ActionError = chatIdResult.error is { } error
                ? DescribeError(error)
                : _localization.GetString("Messenger_ActionFailed");
            return;
        }

        var request = session.ChatSnapshotRequest;
        Chat? definition;
        try
        {
            definition = await _messenger.FindChatAsync(chatIdResult.chatId, token).WaitAsync(token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }
        if (!IsCurrent(session))
        {
            return;
        }

        if (definition is null)
        {
            ActionError = _localization.GetString("Messenger_ActionFailed");
            return;
        }

        var item = Chats.FirstOrDefault(chat => chat.Id == definition.Id);
        if (item is null)
        {
            var created = await CreateChatItemAsync(definition, session.UserId);
            if (!IsCurrent(session))
            {
                return;
            }

            item = Chats.FirstOrDefault(chat => chat.Id == definition.Id);
            if (item is null)
            {
                item = created;
                ObserveMuteHint(session, item, request, definition.Muted);
                UpdateMuteIndicator(session, item);
                UpdateDraftPreview(item);
                Chats.Insert(0, item);
                ApplyChatFilter();
                await TrackPresenceForChatsAsync();
                if (!IsCurrent(session))
                {
                    return;
                }
            }
        }

        ResetSearch();
        SelectedChat = item;
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

        var session = _realtimeSession;
        var key = knownKey ?? await GetPrivateChatKeyAsync(chat);
        if (!IsOpen(session, chat, loadVersion))
        {
            return;
        }

        if (key is null)
        {
            ShowPrivateUnlock(chat);
            return;
        }

        await LoadFeedSnapshotAsync(chat, loadVersion, key);
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
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(SelectedChat, chat))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                _pendingReadMessageIds.ExceptWith(messageIds);
                ReportBackgroundError(result);
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
            _realtimeSession?.Journal.TouchUnread(chat.Id, chat.IsPrivate);
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

    private async Task<ChatItemViewModel> CreateChatItemAsync(Chat chat, long currentUserId, ChatItemViewModel? cached = null)
    {
        var title = string.IsNullOrWhiteSpace(chat.Title) ? _localization.GetString("Messenger_Chat") : chat.Title;
        var firstName = string.Empty;
        var lastName = string.Empty;
        var username = string.Empty;
        var accountStatus = BarkFluff.Proto.Users.AccountStatus.Active;
        long? peerUserId = null;
        // Chat.Picture сервер отдаёт готовой ссылкой на файл, а не идентификатором,
        // поэтому её нельзя разрешать через Files — она уже готова к показу.
        var avatarUrl = chat.Picture;

        string? avatarFileId = null;
        if (!chat.IsGroupChat)
        {
            var otherMember = chat.Members.FirstOrDefault(member => member.UserId != currentUserId);
            if (otherMember is not null)
            {
                peerUserId = otherMember.UserId;
                var userResult = await _messenger.GetUserDataAsync(otherMember.UserId);
                if (userResult is not { Error.IsSuccess: true, Data: not null } && cached is not null)
                    return cached;
                if (userResult is { Error.IsSuccess: true, Data: not null })
                {
                    accountStatus = userResult.Data.AccountStatus;
                    if (accountStatus is BarkFluff.Proto.Users.AccountStatus.Blocked
                        or BarkFluff.Proto.Users.AccountStatus.PendingDeletion
                        or BarkFluff.Proto.Users.AccountStatus.Deleted)
                    {
                        title = _localization.GetString(accountStatus == BarkFluff.Proto.Users.AccountStatus.Blocked
                            ? "Messenger_UserBlocked"
                            : "Messenger_UserDeleted");
                        firstName = title;
                        avatarUrl = string.Empty;
                    }
                    else
                    {
                        firstName = userResult.Data.FirstName;
                        lastName = userResult.Data.LastName;
                        username = userResult.Data.Username;
                        title = string.Join(' ', new[] { firstName, lastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
                        title = string.IsNullOrWhiteSpace(title) ? _localization.GetString("Messenger_Chat") : title;
                        avatarUrl = userResult.Data.ProfilePicturePreviewUrl;
                        if (Guid.TryParse(userResult.Data.ProfilePictureUrl, out _))
                            avatarFileId = userResult.Data.ProfilePictureUrl;
                        if (Guid.TryParse(avatarUrl, out _))
                        {
                            avatarFileId = avatarUrl;
                            avatarUrl = await _messenger.ResolveFileUrlAsync(avatarFileId);
                        }
                    }
                }
            }
        }

        return new ChatItemViewModel(chat, title, firstName, lastName, avatarUrl, peerUserId, _localization, username,
            accountStatus, avatarFileId);
    }

    private async Task<MessageItemViewModel> CreateMessageItemAsync(MessageModel message, long currentUserId)
    {
        // Пересланные приходят отдельными вложениями — их может быть несколько, и каждое
        // рисуется своим блоком: схлопнуть пачку в один значит потерять всё, кроме первого.
        var forwards = message.Attachments
            .Where(attachment => attachment.Type == MessageAttachmentType.ForwardedMessage && attachment.ForwardedMessage is not null)
            .Select(attachment => attachment.ForwardedMessage!)
            .OrderBy(forwarded => forwarded.Order)
            .Select(CreateForwardedContent)
            .ToArray();
        var attachments = await Task.WhenAll(message.Attachments
            .Where(attachment => attachment.Type != MessageAttachmentType.ForwardedMessage)
            .Select(CreateAttachmentItemAsync));

        return new MessageItemViewModel(
            this,
            message,
            message.SenderId == currentUserId,
            attachments,
            currentUserId,
            forwards,
            CreateReplyContent(message.ReplyTo));
    }

    private ForwardedContentViewModel CreateForwardedContent(ForwardedMessageModel forwarded)
    {
        var preview = string.IsNullOrWhiteSpace(forwarded.Text)
            ? _localization.GetString("Messenger_Attachment")
            : MarkdownText.Strip(forwarded.Text);
        return new ForwardedContentViewModel(forwarded.AuthorName, forwarded.OriginalMessageId, preview);
    }

    private ReplyContentViewModel? CreateReplyContent(ReplyPreviewModel? reply)
    {
        if (reply is null)
        {
            return null;
        }

        // У удалённого оригинала сервер не отдаёт ни текст, ни автора — цитата не должна
        // оставаться способом его прочитать, и переходить по ней некуда.
        if (reply.IsDeleted)
        {
            return new ReplyContentViewModel(0, _localization.GetString("Messenger_MessageDeleted"), string.Empty, true);
        }

        var preview = string.IsNullOrWhiteSpace(reply.TextPreview)
            ? _localization.GetString("Messenger_Attachment")
            : MarkdownText.Strip(reply.TextPreview);
        return new ReplyContentViewModel(reply.MessageId, reply.SenderName, preview, false);
    }

    private MessageItemViewModel CreatePrivateMessageItem(PrivateMessageModel message, long currentUserId) =>
        new(this, message, PrivateMessageText(message), message.SenderId == currentUserId, currentUserId);

    private string PrivateMessageText(PrivateMessageModel message) => message.IsDeleted
        ? _localization.GetString("Messenger_PrivateDeleted")
        : message.DecryptionFailed
            ? _localization.GetString("Messenger_PrivateDecryptionFailed")
            : message.Text;


    private async Task<MessageAttachmentItemViewModel> CreateAttachmentItemAsync(AttachmentsModel attachment)
    {
        var fileUrl = await ResolveFileUrlAsync(attachment.FileId);
        var previewUrl = attachment.PreviewUrl;
        if (attachment.Type == MessageAttachmentType.Gif && !string.IsNullOrWhiteSpace(fileUrl))
        {
            // Серверное превью GIF — JPEG. Для анимации ленте нужен оригинал.
            previewUrl = fileUrl;
        }
        else if ((attachment.Type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif ||
                  attachment.Type == MessageAttachmentType.Sticker && string.IsNullOrWhiteSpace(fileUrl))
            && string.IsNullOrWhiteSpace(previewUrl))
        {
            previewUrl = await ResolveFileUrlAsync(attachment.PreviewFileId);
        }

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
    /// Лента отсортирована по времени отправки и идентификатору, как история обычного чата. Сообщение из стрима может обогнать ответ
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
        while (index > 0 && Position(Messages[index - 1]).CompareTo(Position(message)) > 0)
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

    /// <summary>
    /// Чат с новым сообщением поднимается наверх с сохранением выбора в привязанном списке.
    /// </summary>
    private void MoveChatToTop(ChatItemViewModel chat)
    {
        var index = Chats.IndexOf(chat);
        if (index > 0)
        {
            Chats.Move(index, 0);
        }

        ApplyChatFilter();
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

    private void OnRealtimeConnectionChanged(RealtimeSession scope, bool isConnected) =>
        PostSession(scope, session =>
        {
            BeginFeedCatchUp();
            _isRealtimeConnected = isConnected;
            if (isConnected)
                _ = _outgoing.WakeAsync(OutboxScope(session), session.Lifetime.Token);
            session.Resync.SetOnline(isConnected);
            session.ChatRefresh.SetOnline(isConnected);
            session.PinRefresh.SetOnline(isConnected);
            IsReconnecting = !_isRealtimeConnected || !_isPresenceConnected;
        });

    private void OnPresenceConnectionChanged(object? sender, bool isConnected) =>
        _uiDispatcher.Post(() =>
        {
            _isPresenceConnected = isConnected;
            IsReconnecting = !_isRealtimeConnected || !_isPresenceConnected;
        });

    private void OnMessageRead(RealtimeSession scope, MessageReadReceipt receipt)
    {
        PostSession(scope, session =>
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

            session.Journal.TouchUnread(receipt.ChatId, false);
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

    private void OnPrivateMessageRead(RealtimeSession scope, PrivateMessageReadReceipt receipt)
    {
        PostSession(scope, session =>
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

            session.Journal.TouchUnread(receipt.ChatId, true);
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
        var session = _realtimeSession;
        if (session is null || !IsCurrent(session))
        {
            return;
        }

        var token = session.Lifetime.Token;
        var revision = session.Journal.UnreadRevision(chat.Id, chat.IsPrivate);
        (ErrorReturner error, List<Chat>? chats) result;
        try
        {
            result = await _messenger.GetFirstChatPageAsync(token).WaitAsync(token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            if (!IsCurrent(session) || revision != session.Journal.UnreadRevision(chat.Id, chat.IsPrivate))
            {
                return;
            }
            if (!result.error.IsSuccess || result.chats is null)
            {
                ReportBackgroundError(result.error);
                return;
            }

            var updatedChat = result.chats.FirstOrDefault(item => item.Id == chat.Id);
            if (updatedChat is not null)
            {
                chat.UnreadCount = updatedChat.CountUnread;
                chat.FirstUnreadMessageId = updatedChat.FirstUnreadMessageId;
            }
        });
    }
}

public sealed class UserSearchResultViewModel
{
    public UserSearchResultViewModel(UserData user, ILocalizationService localization)
    {
        UserId = user.Id;
        IsRestricted = user.AccountStatus is BarkFluff.Proto.Users.AccountStatus.Blocked
            or BarkFluff.Proto.Users.AccountStatus.PendingDeletion
            or BarkFluff.Proto.Users.AccountStatus.Deleted;
        if (IsRestricted)
        {
            Username = string.Empty;
            UsernameLabel = string.Empty;
            AvatarUrl = string.Empty;
            DisplayName = localization.GetString(user.AccountStatus == BarkFluff.Proto.Users.AccountStatus.Blocked
                ? "Messenger_UserBlocked"
                : "Messenger_UserDeleted");
            return;
        }

        Username = user.Username;
        DisplayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
        UsernameLabel = string.IsNullOrWhiteSpace(Username) ? string.Empty : $"@{Username}";
        AvatarUrl = string.IsNullOrWhiteSpace(user.ProfilePictureUrl)
            ? user.ProfilePicturePreviewUrl
            : user.ProfilePictureUrl;
    }

    public long UserId { get; }
    public string DisplayName { get; }
    public string Username { get; }
    public string UsernameLabel { get; }
    public string AvatarUrl { get; }
    public bool IsRestricted { get; }
}

public sealed partial class ChatItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    public ChatItemViewModel(
        Chat chat,
        string title,
        string firstName,
        string lastName,
        string avatarUrl,
        long? peerUserId,
        ILocalizationService localization,
        string username = "",
        BarkFluff.Proto.Users.AccountStatus accountStatus = BarkFluff.Proto.Users.AccountStatus.Active,
        string? avatarFileId = null)
    {
        _localization = localization;
        Definition = chat;
        Id = chat.Id;
        Title = title;
        FirstName = firstName;
        LastName = lastName;
        Username = username;
        AvatarUrl = avatarUrl;
        AvatarFileId = avatarFileId;
        PeerUserId = peerUserId;
        AccountStatus = accountStatus;
        IsPrivate = chat.ChatType == ChatType.Private;
        ApplySnapshot(chat);
    }

    partial void OnPreviewChanged(string value)
    {
        if (!(_localDraft?.HasContent ?? _serverHasDraft))
            NotifyDraftPreviewChanged();
    }

    internal long LastMessageId { get; private set; }
    internal DateTimeOffset? LastMessageEditedAt { get; private set; }
    internal long SnapshotLastMessageId { get; private set; }
    internal DateTimeOffset? SnapshotLastActivity { get; private set; }

    internal void ApplySnapshot(Chat chat, bool applyPreview = true, bool applyUnread = true)
    {
        if (!ReferenceEquals(Definition, chat))
        {
            var wasAccepted = IsPrivateAccepted;
            Definition = chat;
            if (wasAccepted != IsPrivateAccepted)
                OnPropertyChanged(nameof(IsPrivateAccepted));
        }

        var previousPreview = ListPreview;
        _serverHasDraft = chat.ChatType == ChatType.Regular && chat.HasDraft;
        if (previousPreview != ListPreview)
            NotifyDraftPreviewChanged();

        if (applyPreview)
        {
            SnapshotLastMessageId = chat.LastMessage?.Id ?? 0;
            SnapshotLastActivity = chat.LastActivityAt?.ToDateTimeOffset();
            SetLastMessage(SnapshotLastMessageId, chat.LastMessage?.Content?.Text ?? string.Empty,
                SnapshotLastActivity, chat.LastMessage?.EditedAt?.ToDateTimeOffset(),
                chat.LastMessage?.Content?.Attachments.FirstOrDefault()?.Type);
        }

        if (applyUnread)
        {
            UnreadCount = chat.CountUnread;
            FirstUnreadMessageId = chat.FirstUnreadMessageId;
        }
    }

    internal void SetLastMessage(long id, string text, DateTimeOffset? sentAt, DateTimeOffset? editedAt = null,
        MessageAttachmentType? attachmentType = null)
    {
        LastMessageId = id;
        Preview = IsPrivate ? string.Empty : string.IsNullOrWhiteSpace(text) && attachmentType is not null
            ? _localization.GetString(attachmentType switch
            {
                MessageAttachmentType.Image => "Messenger_AttachmentPhoto",
                MessageAttachmentType.Gif => "Messenger_AttachmentGif",
                MessageAttachmentType.Video => "Messenger_AttachmentVideo",
                MessageAttachmentType.Voice => "Messenger_VoiceAttachment",
                MessageAttachmentType.Audio => "Messenger_AttachmentAudio",
                MessageAttachmentType.Document => "Messenger_AttachmentFile",
                _ => "Messenger_Attachment"
            })
            : CreatePreview(text, 20);
        LastMessageAt = sentAt;
        LastMessageEditedAt = editedAt;
    }

    public string Id { get; }
    internal Chat Definition { get; private set; }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string FirstName { get => _firstName; private set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; private set => SetProperty(ref _lastName, value); }
    public string Username { get => _username; private set => SetProperty(ref _username, value); }
    internal string? AvatarFileId { get; private set; }
    public string AvatarUrl { get => _avatarUrl; private set => SetProperty(ref _avatarUrl, value); }
    private string _title = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _username = string.Empty;
    private string _avatarUrl = string.Empty;

    /// <summary>Собеседник личного чата; <c>null</c> у групповых — им присутствие не показывается.</summary>
    public long? PeerUserId { get; }
    public BarkFluff.Proto.Users.AccountStatus AccountStatus { get; private set; }

    internal void ApplyDisplay(ChatItemViewModel display)
    {
        var initials = Initials;
        Title = display.Title;
        FirstName = display.FirstName;
        LastName = display.LastName;
        Username = display.Username;
        AvatarUrl = display.AvatarUrl;
        AvatarFileId = display.AvatarFileId;
        if (Initials != initials)
            OnPropertyChanged(nameof(Initials));
        if (AccountStatus == display.AccountStatus)
            return;
        AccountStatus = display.AccountStatus;
        OnPropertyChanged(nameof(AccountStatus));
        OnPropertyChanged(nameof(IsRestricted));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(RestrictionMessage));
        OnPropertyChanged(nameof(HasPresence));
    }
    public bool IsRestricted => AccountStatus is BarkFluff.Proto.Users.AccountStatus.Blocked
        or BarkFluff.Proto.Users.AccountStatus.PendingDeletion
        or BarkFluff.Proto.Users.AccountStatus.Deleted;
    public bool CanSend => !IsRestricted;
    public string RestrictionMessage => !IsRestricted
        ? string.Empty
        : _localization.GetString(AccountStatus == BarkFluff.Proto.Users.AccountStatus.Blocked
            ? "Messenger_ReadOnlyBlocked"
            : "Messenger_ReadOnlyDeleted");

    // Превью и время последней активности переписывает входящее сообщение, поэтому они
    // наблюдаемые, а не вычисленные один раз в конструкторе.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _preview = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastMessageAtLabel))]
    private DateTimeOffset? _lastMessageAt;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadMessages), nameof(HasMutedUnreadMessages), nameof(HasUnmutedUnreadMessages))]
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
    public bool HasPresence => PeerUserId is not null && !IsRestricted;
    public string PresenceLabel => LastSeenFormatter.Format(_localization, IsOnline, LastSeen, DateTimeOffset.Now);

    /// <summary>В WinUI нет <c>StringFormat</c>, поэтому время приходит в разметку готовой строкой.</summary>
    public string LastMessageAtLabel => LastMessageAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;
    public bool HasPreview => !string.IsNullOrWhiteSpace(Preview);
    public bool HasUnreadMessages => UnreadCount > 0;
    public string Initials => string.Concat((FirstName + " " + LastName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(name => name[0])).ToUpperInvariant() is { Length: > 0 } initials
        ? initials
        : Title[..1].ToUpperInvariant();

    /// <summary>
    /// Учитывает новое непрочитанное сообщение и запоминает границу непрочитанного.
    /// </summary>
    internal void CountIncomingUnread(long messageId)
    {
        UnreadCount++;
        if (FirstUnreadMessageId == 0)
        {
            FirstUnreadMessageId = messageId;
        }
    }

    /// <summary>
    /// Убирает markdown-разметку, сжимает текст в одну строку и обрезает по числу видимых символов.
    /// </summary>
    internal static string CreatePreview(string text, int maxElements)
    {
        var normalized = string.Join(' ', MarkdownText.Strip(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elementStarts = StringInfo.ParseCombiningCharacters(normalized);
        return elementStarts.Length <= maxElements
            ? normalized
            : normalized[..elementStarts[maxElements - 1]].TrimEnd() + "…";
    }
}

public sealed partial class MessageItemViewModel : ObservableObject
{
    private readonly long _currentUserId;

    public MessageItemViewModel(MessengerViewModel owner, MessageModel message, bool isMine, IReadOnlyCollection<MessageAttachmentItemViewModel> attachments, long currentUserId, IReadOnlyCollection<ForwardedContentViewModel> forwards, ReplyContentViewModel? reply)
    {
        Owner = owner;
        Forwards = forwards;
        Reply = reply;
        Id = message.MessageId;
        Text = message.Text;
        IsMine = isMine;
        IsSystem = message.Type == MessageContentType.System;
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = attachments;
        MediaAttachments = attachments.Where(attachment => attachment.IsMedia && !attachment.IsSticker).ToArray();
        FileAttachments = attachments.Where(attachment => attachment.IsFile).ToArray();
        _currentUserId = currentUserId;
        IsEdited = message.IsEdited;
        EditedAt = message.EditedAt?.ToDateTimeOffset();
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
        IsEdited = message.IsEdited;
        EditedAt = message.EditedAt?.ToDateTimeOffset();
        SentAt = message.SentAt.ToDateTimeOffset();
        Attachments = Array.Empty<MessageAttachmentItemViewModel>();
        MediaAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        FileAttachments = Array.Empty<MessageAttachmentItemViewModel>();
        _currentUserId = currentUserId;
    }

    public MessengerViewModel Owner { get; }
    /// <summary>Пересланные сообщения в порядке отправителя. Пусто, если пересылки нет.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForwards), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(IsStickerOnly), nameof(UseCompactBubblePadding), nameof(UseOwnBubbleText))]
    private IReadOnlyCollection<ForwardedContentViewModel> _forwards = [];

    /// <summary>Цитата ответа. Заполнена => это ответ. Резолвится сервером, не снапшот.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReply), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia),
        nameof(IsStickerOnly), nameof(UseCompactBubblePadding), nameof(UseOwnBubbleText))]
    private ReplyContentViewModel? _reply;

    public long Id { get; }
    public bool IsMine { get; }
    public bool IsSystem { get; }
    public bool IsPrivateMessage { get; }
    public DateTimeOffset SentAt { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StickerAttachments), nameof(HasStickers), nameof(IsStickerOnly), nameof(UseCompactBubblePadding),
        nameof(UseOwnBubbleText), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(CanCopyImage))]
    private IReadOnlyCollection<MessageAttachmentItemViewModel> _attachments = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(UseCompactBubblePadding), nameof(IsSingleMedia),
        nameof(MediaColumns), nameof(MediaPanelWidth), nameof(MediaTileHeight), nameof(CanCopyImage))]
    private IReadOnlyCollection<MessageAttachmentItemViewModel> _mediaAttachments = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(UseCompactBubblePadding))]
    private IReadOnlyCollection<MessageAttachmentItemViewModel> _fileAttachments = [];

    internal DateTimeOffset? EditedAt { get; private set; }
    internal long AppliedUpdateRevision { get; set; }

    internal void ApplyContent(MessageItemViewModel item)
    {
        Text = item.Text;
        IsEdited = item.IsEdited;
        EditedAt = item.EditedAt;
        AppliedUpdateRevision = item.AppliedUpdateRevision;
        Reply = item.Reply;
        Forwards = item.Forwards;
        Attachments = item.Attachments;
        MediaAttachments = item.MediaAttachments;
        FileAttachments = item.FileAttachments;
        IsReadByCurrentUser |= item.IsReadByCurrentUser;
        IsReadBySomeoneElse |= item.IsReadBySomeoneElse;
    }
    /// <summary>В WinUI нет <c>StringFormat</c>, поэтому время приходит в разметку готовой строкой.</summary>
    public string SentAtLabel => SentAt.ToLocalTime().ToString("HH:mm");
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasMedia => MediaAttachments.Count > 0;
    public bool HasFiles => FileAttachments.Count > 0;
    public IReadOnlyCollection<MessageAttachmentItemViewModel> StickerAttachments => Attachments.Where(attachment => attachment.IsSticker).ToArray();
    public bool HasStickers => Attachments.Any(attachment => attachment.IsSticker);
    public bool IsStickerOnly => !HasText && !HasReply && !HasForwards &&
        Attachments.Count == 1 && Attachments.First().IsSticker;
    public bool UseCompactBubblePadding => IsStickerOnly || HasOnlyMedia;
    public bool UseOwnBubbleText => IsMine && !IsStickerOnly;
    public bool HasOnlyMedia => !HasText && HasMedia && !HasFiles && !HasForwards && !HasReply && !HasStickers;
    public bool HasMetadataBelowMedia => !HasOnlyMedia;
    public bool IsSingleMedia => MediaAttachments.Count == 1;
    public int MediaColumns => IsSingleMedia ? 1 : 2;
    public double MediaPanelWidth => IsSingleMedia ? MediaAttachments.First().SingleMediaWidth : 344;
    public double MediaTileHeight => IsSingleMedia ? MediaAttachments.First().SingleMediaHeight : 128;

    // Приватные (E2E) и системные сообщения действий не поддерживают — как в веб-версии.
    public bool CanUseActions => !IsSystem && !IsPrivateMessage;
    public bool CanModify => CanUseActions && IsMine;
    public bool CanCopyText => CanUseActions && HasText;
    public bool CanCopyImage => CanUseActions && !HasStickers && IsSingleMedia && MediaAttachments.First().IsImageOrGif;
    public string PinMenuHeader => Owner.Localization.GetString(IsPinned ? "Messenger_Unpin" : "Messenger_Pin");
    public bool HasReply => Reply is not null;
    public bool HasForwards => Forwards.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText), nameof(HasOnlyMedia), nameof(HasMetadataBelowMedia), nameof(CanCopyText),
        nameof(IsStickerOnly), nameof(UseCompactBubblePadding), nameof(UseOwnBubbleText))]
    private string _text = string.Empty;
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

/// <summary>
/// Цитата ответа. Отдельный тип от <see cref="ForwardedContentViewModel"/>, потому что это
/// не снапшот: содержимое резолвится сервером из живого оригинала, а у удалённого его нет.
/// </summary>
public sealed class ReplyContentViewModel(long messageId, string senderName, string preview, bool isDeleted)
{
    public long MessageId { get; } = messageId;
    public string SenderName { get; } = senderName;
    public string Preview { get; } = preview;
    public bool IsDeleted { get; } = isDeleted;
}

public sealed partial class MessageAttachmentItemViewModel(
    MessageAttachmentType type,
    string previewUrl,
    string fileUrl,
    string fileName,
    long size,
    int imageWidth,
    int imageHeight) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VoicePlaybackSymbol))] private bool _isVoiceActive;
    public string VoicePlaybackSymbol => IsVoiceActive ? "■" : "▶";
    [ObservableProperty] private double _voiceProgress;
    [ObservableProperty] private string? _voiceTime;
    [ObservableProperty] private string? _voiceError;

    internal MessageAttachmentType Type => type;
    public string PreviewUrl { get; } = previewUrl;
    public string FileUrl { get; } = fileUrl;
    public string FileName { get; } = fileName;
    public long Size { get; } = size;
    public int ImageWidth { get; } = imageWidth;
    public int ImageHeight { get; } = imageHeight;
    public bool IsSticker => type == MessageAttachmentType.Sticker;
    public string StickerUrl => FileUrl.Length > 0 ? FileUrl : PreviewUrl;
    public bool IsVideo { get; } = type == MessageAttachmentType.Video;
    public bool IsOpusVoice => type == MessageAttachmentType.Voice;
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
