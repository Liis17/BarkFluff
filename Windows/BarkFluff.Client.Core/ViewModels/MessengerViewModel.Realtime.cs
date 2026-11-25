using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private sealed class RealtimeSession(long userId, string node)
    {
        internal readonly HashSet<string> ConfirmedHistoryTails = [];
        internal bool CanWriteHistoryEvents = true;
        internal long UserId { get; } = userId;
        internal bool Loading { get; set; }
        internal bool CacheLoaded { get; set; }
        internal bool CacheWriteScheduled { get; set; }
        // Снимок из realtime может быть неполным до первой полной загрузки или после очистки.
        internal bool CanWriteCache { get; set; }
        internal HashSet<string> CachedChatIds { get; } = [];
        internal string Node { get; } = node;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal MessageUpdateJournal Journal { get; } = new();
        internal Dictionary<(string ChatId, long MessageId), List<(MessageUpdate Update, bool FromDelivery)>> ConfirmingMessages { get; } = [];
        internal ResyncLoop Resync { get; set; } = null!;
        internal ResyncLoop ChatRefresh { get; set; } = null!;
        internal ResyncLoop PinRefresh { get; set; } = null!;
        internal bool ReportChatRefreshError { get; set; }
        internal bool MessageSearchUnavailable { get; set; }
        internal long ChatSnapshotRequest { get; set; }
        internal Dictionary<string, DateTimeOffset?> MutedChats { get; set; } = [];
        internal Dictionary<string, long>? PendingMuteHints { get; set; }
        internal Task<bool>? ChatSnapshot { get; set; }
        internal Dictionary<long, Task<string?>> NotificationSenders { get; } = [];
        internal TaskCompletionSource NotificationMutesReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HasAuthoritativeMutes { get; set; }
        internal ITimer? MuteExpiryTimer { get; set; }
        internal Action Unsubscribe { get; set; } = null!;
    }

    private RealtimeSession? _realtimeSession;
    internal Func<TimeSpan, CancellationToken, Task> ResyncDelay { get; set; } = Task.Delay;
    private long _pinnedRevision;

    private bool IsCurrent(RealtimeSession? session) => session is not null &&
        ReferenceEquals(session, _realtimeSession) && session.UserId == _messenger.CurrentUserId &&
        session.Node == _messenger.CurrentNodeAddress;

    private bool IsOpen(RealtimeSession? session, ChatItemViewModel chat, int version) =>
        IsCurrent(session) && ReferenceEquals(SelectedChat, chat) && version == _chatVersion;

    private void StartRealtimeSession(long userId)
    {
        if (IsCurrent(_realtimeSession))
        {
            return;
        }

        EndRealtimeSession();
        var session = new RealtimeSession(userId, _messenger.CurrentNodeAddress);
        session.Resync = new ResyncLoop(token => ResyncAsync(session, token), session.Lifetime.Token) { Delay = ResyncDelay };
        session.ChatRefresh = new ResyncLoop(token => RefreshChatsAsync(session, reportError: false, token),
            session.Lifetime.Token) { Delay = ResyncDelay };
        session.PinRefresh = new ResyncLoop(_ => SelectedChat is { IsPrivate: false } chat && IsCurrent(session)
            ? RefreshPinsAsync(chat, _chatVersion, reportError: false)
            : Task.FromResult(true), session.Lifetime.Token) { Delay = ResyncDelay };
        _realtimeSession = session;
        StartSecretChats(session);
        StartDraftSession(session);
        StartOutgoing(session);
        _stickerFrames?.StartSession();
        StickerPicker?.StartSession(session.Node, session.UserId, session.Lifetime.Token, () => IsCurrent(session));
        _notificationSettings?.StartSession(() => IsCurrent(session), session.Lifetime.Token);
        EventHandler foldersChanged = (_, _) => PostSession(session, _ => _chatFolders?.Invalidate());
        _realtimeMessenger.ChatFoldersChanged += foldersChanged;
        EventHandler<IncomingMessage> received = (_, incoming) => OnMessageChanged(session,
            new(incoming.ChatId, false, incoming.Message.MessageId, MessageUpdateKind.New, Message: incoming.Message));
        EventHandler<MessageUpdate> changed = (_, update) => OnMessageChanged(session, update);
        EventHandler<bool> resync = (_, immediate) => OnResyncRequested(session, immediate);
        EventHandler<bool> connection = (_, connected) => OnRealtimeConnectionChanged(session, connected);
        EventHandler<MessageReadReceipt> read = (_, receipt) => OnMessageRead(session, receipt);
        EventHandler<PrivateMessageReadReceipt> privateRead = (_, receipt) => OnPrivateMessageRead(session, receipt);
        _realtimeMessenger.MessageReceived += received;
        _realtimeMessenger.MessageChanged += changed;
        _realtimeMessenger.ResyncRequested += resync;
        _realtimeMessenger.ConnectionChanged += connection;
        _realtimeMessenger.MessageRead += read;
        _realtimeMessenger.PrivateMessageRead += privateRead;
        var unsubscribeCache = ObserveChatCache(session);
        var unsubscribeUnreadBadge = ObserveUnreadBadge(session);
        var unsubscribeFolderTabs = ObserveFolderTabs(session);
        session.Unsubscribe = () =>
        {
            unsubscribeCache();
            unsubscribeUnreadBadge();
            unsubscribeFolderTabs();
            _realtimeMessenger.ChatFoldersChanged -= foldersChanged;
            _realtimeMessenger.MessageReceived -= received;
            _realtimeMessenger.MessageChanged -= changed;
            _realtimeMessenger.ResyncRequested -= resync;
            _realtimeMessenger.ConnectionChanged -= connection;
            _realtimeMessenger.MessageRead -= read;
            _realtimeMessenger.PrivateMessageRead -= privateRead;
        };
    }

    private void EndRealtimeSession()
    {
        MediaViewer.Close();
        var session = _realtimeSession;
        CloseCallHistory();
        _calls?.Stop();
        _lookupCallId = Guid.Empty;
        ResetSearch();
        _ = CancelAttachmentsAsync();
        _stickerFrames?.EndSession();
        StickerPicker?.Reset();
        if (session is null)
        {
            return;
        }

        StopSecretChats();
        EndOutgoing();
        _realtimeSession = null;
        _ = StopDraftsAsync();
        _notificationSettings?.EndSession();
        _notifications?.EndSession();
        session.MuteExpiryTimer?.Dispose();
        session.Unsubscribe();
        _chatFolders?.Reset();
        RefreshFolderTabs([]);
        FolderActionError = null;
        UnreadBadgeCount = 0;
        session.Lifetime.Cancel();
        session.Lifetime.Dispose();
    }

    private void PostSession(RealtimeSession session, Action<RealtimeSession> action)
    {
        _uiDispatcher.Post(() =>
        {
            if (IsCurrent(session))
            {
                action(session);
            }
        });
    }

    private void OnResyncRequested(RealtimeSession scope, bool immediate) => PostSession(scope, session =>
    {
        session.ConfirmedHistoryTails.Clear();
        _notificationSettings?.RetryInitialLoad();
        BeginFeedCatchUp();
        if (immediate)
        {
            session.Resync.RequestImmediate();
        }
        else
        {
            session.Resync.RequestConfirmation();
        }
    });

    private void OnMessageChanged(RealtimeSession session, MessageUpdate update, bool fromOutgoingDelivery = false)
    {
        PostSession(session, scope =>
        {
            var key = (update.ChatId, update.MessageId);
            if (!update.IsPrivate && scope.ConfirmingMessages.TryGetValue(key, out var pending))
            {
                pending.Add((update, fromOutgoingDelivery));
                return;
            }

            if (!fromOutgoingDelivery && update is { Kind: MessageUpdateKind.New, IsPrivate: false, Message: { } confirmed })
            {
                var confirmation = ConfirmOutgoingAsync(scope, update.ChatId, confirmed);
                if (!confirmation.IsCompletedSuccessfully)
                {
                    scope.ConfirmingMessages.Add(key, [(update, false)]);
                    _ = ApplyAfterConfirmationAsync(confirmation);
                    return;
                }
            }
            _ = ApplyMessageUpdateAsync(scope, update, fromOutgoingDelivery);
        });

        async Task ApplyAfterConfirmationAsync(Task confirmation)
        {
            await confirmation.ConfigureAwait(false);
            PostSession(session, scope =>
            {
                var key = (update.ChatId, update.MessageId);
                var pending = scope.ConfirmingMessages[key];
                scope.ConfirmingMessages.Remove(key);
                foreach (var (deferred, fromDelivery) in pending)
                    _ = ApplyMessageUpdateAsync(scope, deferred, fromDelivery);
            });
        }
    }

    private async Task ApplyMessageUpdateAsync(RealtimeSession session, MessageUpdate update, bool fromOutgoingDelivery = false)
    {
        var chat = Chats.FirstOrDefault(item => item.Id == update.ChatId);
        if (chat is not null && chat.IsPrivate != update.IsPrivate)
        {
            return;
        }

        if (update.Kind is MessageUpdateKind.Pin or MessageUpdateKind.Unpin or MessageUpdateKind.UnpinAll)
        {
            if (chat is not null && chat == SelectedChat && !chat.IsPrivate)
                ApplyPinUpdate(session, update);
            if (chat?.Definition.ChatType == ChatType.Regular && session.CanWriteHistoryEvents &&
                     HistoryCacheGeneration(session, chat) is { } epoch &&
                     session.Journal.Find(chat.Id, false, update.MessageId)?.Kind != MessageUpdateKind.Delete)
            {
                _chatCache!.History!.Schedule(CacheScope(session), epoch, new(chat.Id, [], PinChange: update));
            }
            return;
        }

        if (update.Encrypted?.IsDeleted == true)
        {
            update = update with { Kind = MessageUpdateKind.Delete, Encrypted = null };
        }

        var existing = chat == SelectedChat && chat is not null
            ? Messages.FirstOrDefault(item => item.Id == update.MessageId)
            : null;
        if (update.Kind == MessageUpdateKind.Edit &&
            existing?.EditedAt > MessageUpdateJournal.EditedAt(update)?.ToDateTimeOffset())
        {
            return;
        }

        var change = session.Journal.Record(update);
        if (change is null)
        {
            return;
        }

        SaveHistoryEvent(session, chat, update, change.Payload);
        try
        {
            var applying = ApplyRecordedUpdateAsync(session, chat, update, change, existing, fromOutgoingDelivery);
            _ = NotifyIncomingAsync(session, update, chat);
            await applying;
        }
        finally
        {
            session.Journal.Release(update.ChatId, update.IsPrivate, change);
        }
    }

    private async Task ApplyRecordedUpdateAsync(RealtimeSession session, ChatItemViewModel? chat,
        MessageUpdate update, MessageUpdateJournal.Change change, MessageItemViewModel? existing, bool fromOutgoingDelivery)
    {
        if (chat is null)
        {
            session.ReportChatRefreshError = true;
            if (update.Kind == MessageUpdateKind.Delete)
            {
                session.ChatRefresh.RequestConfirmation();
            }
            else
            {
                session.ChatRefresh.RequestImmediate();
            }
            return;
        }

        var isOpen = chat == SelectedChat;
        change.WaitingForNew = isOpen && existing is null && !change.SawNew && update.Kind == MessageUpdateKind.Edit;
        var version = _chatVersion;
        var window = _historyWindowLifetime.Token;
        var effective = change.Payload;
        if (change.Kind == MessageUpdateKind.Delete)
        {
            var wasFirstUnread = chat.FirstUnreadMessageId == update.MessageId;
            if (isOpen)
            {
                RemoveMessage(chat, update.MessageId, adjustUnread: true);
            }

            var wasLast = chat.LastMessageId == update.MessageId;
            if (wasLast)
            {
                var last = isOpen && _feedIncludesTail ? Messages.LastOrDefault() : null;
                chat.SetLastMessage(last?.Id ?? 0, last?.Text ?? string.Empty, last?.SentAt, last?.EditedAt,
                    last?.Attachments.FirstOrDefault()?.Type);
            }

            if (wasLast || wasFirstUnread || existing is null && chat.UnreadCount > 0)
            {
                session.ChatRefresh.RequestConfirmation();
            }
            return;
        }

        if (update.Kind == MessageUpdateKind.New)
        {
            var sender = update.Message?.SenderId ?? update.Encrypted!.SenderId;
            var sentAt = (update.Message?.SentAt ?? update.Encrypted!.SentAt).ToDateTimeOffset();
            var loaded = isOpen && Messages.Any(item => item.Id == update.MessageId);
            var unread = sender != session.UserId && !loaded && update.Message?.ReadBy.Contains(session.UserId) != true;
            var alreadyInSnapshot = chat.IsPrivate
                ? sentAt <= chat.SnapshotLastActivity
                : update.MessageId == chat.SnapshotLastMessageId || sentAt < chat.SnapshotLastActivity && chat.SnapshotLastMessageId > 0;
            if (unread && !alreadyInSnapshot)
            {
                chat.CountIncomingUnread(update.MessageId);
                if (isOpen && (!chat.IsPrivate || chat.IsPrivateAccepted))
                    CountNewMessageBelow(update.MessageId);
            }

            if ((!chat.IsPrivate && chat.LastMessageId == 0) ||
                (sentAt, update.MessageId).CompareTo((chat.LastMessageAt ?? DateTimeOffset.MinValue, chat.LastMessageId)) > 0)
            {
                if (!chat.IsPrivate && effective is null)
                {
                    session.ChatRefresh.RequestConfirmation();
                }
                chat.SetLastMessage(update.MessageId, effective?.Message?.Text ?? string.Empty, sentAt,
                    change.EditTime?.ToDateTimeOffset(), effective?.Message?.Attachments.FirstOrDefault()?.Type);
                MoveChatToTop(chat);
            }
        }

        if (change.Kind == MessageUpdateKind.Edit && effective?.Message is { } edited)
        {
            if (chat.LastMessageId == edited.MessageId && !(chat.LastMessageEditedAt > edited.EditedAt?.ToDateTimeOffset()))
            {
                chat.SetLastMessage(edited.MessageId, edited.Text, chat.LastMessageAt, edited.EditedAt?.ToDateTimeOffset(),
                    edited.Attachments.FirstOrDefault()?.Type);
            }

            if (isOpen)
            {
                _pinnedRevision++;
                UpdateQuotes(edited.MessageId, edited.Text);
                var pinned = _pinnedMessages.FirstOrDefault(pin => pin.Message?.Id == edited.MessageId);
                if (pinned?.Message?.Content is { } content)
                {
                    content.Text = edited.Text;
                    RefreshPinnedBar();
                    if (session.CanWriteHistoryEvents)
                        SaveHistoryPins(session, chat, HistoryCacheGeneration(session, chat));
                }
            }
        }

        if (!isOpen || effective is null || existing is null && !_feedIncludesTail && !change.WaitingForNew)
        {
            return;
        }

        if (chat.IsPrivate && !chat.IsPrivateAccepted)
        {
            return;
        }

        // Ответ отправки подтверждает конкретную строку в открытом хвосте даже во время сверки.
        if (!fromOutgoingDelivery && existing is null && change.SawNew && _reconnectFeed is { } recovering &&
            !IsInsideLoadedWindow(effective))
        {
            // До сверки неизвестно, прилегает ли вновь полученное сообщение к открытому окну.
            await recovering.Task;
            if (!IsOpenWindow(session, chat, version, window) || !_feedIncludesTail ||
                session.Journal.Find(chat.Id, chat.IsPrivate, update.MessageId) != change ||
                Messages.Any(message => message.Id == update.MessageId && message.AppliedUpdateRevision == change.Revision))
            {
                return;
            }
        }

        // Ссылки на вложения разрешаются отдельными вызовами. За это время чат мог смениться.
        var item = await BuildUpdatedItemAsync(session, chat, effective);
        if (item is null || !IsOpenWindow(session, chat, version, window) ||
            session.Journal.Find(chat.Id, chat.IsPrivate, item.Id) != change)
        {
            return;
        }

        item.AppliedUpdateRevision = change.Revision;
        var inserted = UpsertMessage(item, change.SawNew && _feedIncludesTail);
        if (inserted && _isFeedAtBottom && !IsHistoryUpdating)
        {
            RequestScroll(MessageScrollTarget.Bottom);
        }
    }

    private async Task<MessageItemViewModel?> BuildUpdatedItemAsync(RealtimeSession session, ChatItemViewModel chat, MessageUpdate update)
    {
        if (update.Message is { } message)
        {
            return await CreateMessageItemAsync(message, session.UserId);
        }

        var key = await GetPrivateChatKeyAsync(chat);
        return key is null ? null : CreatePrivateMessageItem(WebApiClient.DecryptPrivateMessage(update.Encrypted!, key), session.UserId);
    }

    private bool UpsertMessage(MessageItemViewModel item, bool allowInsert)
    {
        var current = Messages.FirstOrDefault(message => message.Id == item.Id);
        if (current is null)
        {
            ApplyQuoteChange(item);
            item.IsPinned = _pinnedMessages.Any(pin => pin.Message?.Id == item.Id);
            return allowInsert && InsertMessageInOrder(item);
        }

        if (current.EditedAt > item.EditedAt || (current.IsEdited && !item.IsEdited))
        {
            return false;
        }

        ApplyQuoteChange(item);
        current.ApplyContent(item);
        if (ReferenceEquals(ReplyTarget, current) || ReferenceEquals(EditingMessage, current))
        {
            OnPropertyChanged(nameof(ComposerHintPreview));
        }
        return false;
    }

    private void RemoveMessage(ChatItemViewModel chat, long messageId, bool adjustUnread)
    {
        ForgetNewMessageBelow(messageId);
        _shownUnreadMessages.Remove(messageId);
        _pendingReadMessageIds.Remove(messageId);
        // Сервер сам снимает закреп с удалённого сообщения. Локальный кеш чистим сразу.
        if (!chat.IsPrivate && _pinnedMessages.RemoveAll(pin => pin.Message?.Id == messageId) != 0)
        {
            RefreshPinnedBar();
        }

        if (ReplyTarget?.Id == messageId)
            ReplyTarget = null;

        var existing = Messages.FirstOrDefault(item => item.Id == messageId);
        if (existing is not null)
        {
            Messages.Remove(existing);
            if (EditingMessage?.Id == messageId)
            {
                RestoreDraftAfterEdit();
            }

            if (MessagePendingDelete?.Id == messageId)
            {
                MessagePendingDelete = null;
            }

            if (adjustUnread && !existing.IsMine && !existing.IsReadByCurrentUser)
            {
                UpdateUnreadState(chat, 1);
            }
        }

        UpdateQuotes(messageId, null);
    }

    private void UpdateQuotes(long id, string? text)
    {
        foreach (var item in Messages.Where(item => item.Reply?.MessageId == id))
        {
            SetQuote(item, text);
        }
    }

    private void ApplyQuoteChange(MessageItemViewModel item) => UpdateSnapshotQuote(item, item.Reply);

    private void UpdateSnapshotQuote(MessageItemViewModel item, ReplyContentViewModel? reply)
    {
        if (reply is { IsDeleted: false } && SelectedChat is { IsPrivate: false } chat)
        {
            var original = Messages.FirstOrDefault(message => message.Id == reply.MessageId);
            var change = _realtimeSession?.Journal.Find(chat.Id, false, reply.MessageId);
            if (change?.Kind == MessageUpdateKind.Delete)
            {
                reply = CreateChangedQuote(reply, null);
            }
            else if (original?.IsEdited == true)
            {
                reply = CreateChangedQuote(reply, original.Text);
            }
            else if (change?.Kind == MessageUpdateKind.Edit && change.Payload?.Message is { } edited)
            {
                reply = CreateChangedQuote(reply, edited.Text);
            }
        }

        if (item.Reply?.MessageId != reply?.MessageId || item.Reply?.IsDeleted != reply?.IsDeleted ||
            item.Reply?.SenderName != reply?.SenderName || item.Reply?.Preview != reply?.Preview)
        {
            item.Reply = reply;
        }
    }

    private void SetQuote(MessageItemViewModel item, string? text)
    {
        var reply = CreateChangedQuote(item.Reply!, text);
        if (item.Reply!.IsDeleted != reply.IsDeleted || item.Reply.Preview != reply.Preview)
        {
            item.Reply = reply;
        }
    }

    private ReplyContentViewModel CreateChangedQuote(ReplyContentViewModel reply, string? text) => text is null
        ? new ReplyContentViewModel(0, _localization.GetString("Messenger_MessageDeleted"), string.Empty, true)
        : new ReplyContentViewModel(reply.MessageId, reply.SenderName,
            string.IsNullOrWhiteSpace(text) ? _localization.GetString("Messenger_Attachment") : Markdown.MarkdownText.Strip(text), false);

    private void ApplyPinUpdate(RealtimeSession session, MessageUpdate update)
    {
        _pinnedRevision++;
        switch (update.Kind)
        {
            case MessageUpdateKind.Pin:
                var previous = _pinnedMessages.FirstOrDefault(pin => pin.Message?.Id == update.MessageId);
                if (session.Journal.Find(update.ChatId, false, update.MessageId)?.Kind == MessageUpdateKind.Delete ||
                    previous is not null && Comparer<Google.Protobuf.WellKnownTypes.Timestamp?>.Default.Compare(previous.PinnedAt, update.PinnedAt) >= 0)
                {
                    return;
                }

                var loaded = Messages.FirstOrDefault(message => message.Id == update.MessageId);
                _pinnedMessages.RemoveAll(pin => pin.Message?.Id == update.MessageId);
                _pinnedMessages.Insert(0, new PinnedMessageInfo
                {
                    PinnedAt = update.PinnedAt,
                    PinnerUserId = update.PinnerId,
                    Message = new Message { Id = update.MessageId, Content = new MessageContent { Text = loaded?.Text ?? string.Empty } }
                });
                break;
            case MessageUpdateKind.Unpin:
                if (_pinnedMessages.RemoveAll(pin => pin.Message?.Id == update.MessageId) == 0)
                {
                    return;
                }
                break;
            case MessageUpdateKind.UnpinAll:
                if (_pinnedMessages.Count == 0)
                {
                    return;
                }
                _pinnedMessages.Clear();
                break;
        }

        ApplyPinnedState();
        RefreshPinnedBar();
        if (update.Kind == MessageUpdateKind.Pin && Messages.All(message => message.Id != update.MessageId))
        {
            session.PinRefresh.RequestConfirmation();
        }
    }
}
