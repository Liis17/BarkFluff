using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.WebApi.Core;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private const int SnapshotPageSize = 50;
    private const int AnchorProbesPerPage = 3;
    private TaskCompletionSource? _feedLoadCompletion;
    private TaskCompletionSource? _reconnectFeed;
    private bool _feedLoaded;
    private bool _feedIncludesTail = true;

    private void BeginFeedCatchUp()
    {
        if (_feedLoaded && _feedIncludesTail && SelectedChat is { IsPrivateAccepted: true } or { IsPrivate: false })
        {
            _reconnectFeed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void CompleteFeedLoad(long firstUnread)
    {
        _feedLoaded = true;
        if (firstUnread > 0 && Messages.Any(message => message.Id == firstUnread))
        {
            RequestScroll(MessageScrollTarget.Message, firstUnread);
        }
        else
        {
            RequestScroll(MessageScrollTarget.Bottom);
        }
    }

    private async Task<bool> ResyncAsync(RealtimeSession session, CancellationToken token)
    {
        if (!IsCurrent(session))
        {
            return true;
        }

        var chat = SelectedChat;
        var version = _chatVersion;
        var window = _historyWindowLifetime.Token;
        var reconnectFeed = _reconnectFeed;
        if (_feedLoadCompletion is { } loading)
        {
            await loading.Task.WaitAsync(token);
        }

        if (!IsCurrent(session))
        {
            return true;
        }

        var revision = session.Journal.Revision;
        using var reading = chat is null ? null : session.Journal.ReadHistory(chat.Id, chat.IsPrivate);
        var chatsOk = await RefreshChatsAsync(session, reportError: false, token);
        if (chat is null || !IsOpen(session, chat, version))
        {
            return chatsOk;
        }

        var historyTask = window.IsCancellationRequested ? Task.FromResult(true) :
            ResyncHistoryAsync(session, chat, version, window, revision, reconnectFeed, token);
        var pinsTask = chat.IsPrivate ? Task.FromResult(true) : RefreshPinsAsync(chat, version, reportError: false);
        var results = await Task.WhenAll(historyTask, pinsTask);
        return chatsOk && results.All(success => success);
    }

    private async Task<bool> RefreshChatsAsync(RealtimeSession session, bool reportError, CancellationToken token)
    {
        var revision = session.Journal.Revision;
        var request = ++session.ChatSnapshotRequest;
        var (error, definitions) = await _messenger.GetChatsAsync(token).WaitAsync(token);
        if (!IsCurrent(session) || request != session.ChatSnapshotRequest)
        {
            return true;
        }

        if (!error.IsSuccess || definitions is null)
        {
            if (reportError)
            {
                ActionError = DescribeError(error);
            }
            else if (session.ReportChatRefreshError)
            {
                ReportBackgroundError(error);
            }

            return false;
        }

        var known = Chats.Select(chat => chat.Id).ToHashSet();
        var newItems = await Task.WhenAll(definitions.Where(definition => known.Add(definition.Id))
            .Select(definition => CreateChatItemAsync(definition, session.UserId))).WaitAsync(token);
        if (!IsCurrent(session) || request != session.ChatSnapshotRequest)
        {
            return true;
        }

        // Clear привязанного списка сбросил бы SelectedChat и закрыл переписку.
        foreach (var item in newItems)
        {
            if (!Chats.Any(chat => chat.Id == item.Id))
            {
                Chats.Add(item);
            }
        }

        foreach (var definition in definitions)
        {
            var item = Chats.First(chat => chat.Id == definition.Id);
            item.ApplySnapshot(definition,
                applyPreview: session.Journal.ChatRevision(item.Id, item.IsPrivate) <= revision,
                applyUnread: session.Journal.UnreadRevision(item.Id, item.IsPrivate) <= revision);
        }

        var ordered = Chats.OrderByDescending(chat => chat.LastMessageAt).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var oldIndex = Chats.IndexOf(ordered[index]);
            if (oldIndex != index)
            {
                Chats.Move(oldIndex, index);
            }
        }

        ApplyChatFilter();
        if (newItems.Length != 0 || reportError)
        {
            await TrackPresenceForChatsAsync();
        }
        session.ReportChatRefreshError = false;
        return true;
    }

    private async Task<bool> ResyncHistoryAsync(RealtimeSession session, ChatItemViewModel chat, int version, CancellationToken window,
        long revision, TaskCompletionSource? reconnectFeed, CancellationToken token)
    {
        _isHistoryResyncing = true;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, window);
        bool IsCurrentWindow() => IsOpenWindow(session, chat, version, window);
        try
        {
            return await ReconcileHistoryAsync(session, chat, revision, reconnectFeed, IsCurrentWindow, lifetime.Token);
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
            return true;
        }
        finally
        {
            if (IsCurrentWindow())
            {
                _isHistoryResyncing = false;
            }
        }
    }

    private async Task<bool> ReconcileHistoryAsync(RealtimeSession session, ChatItemViewModel chat,
        long revision, TaskCompletionSource? reconnectFeed, Func<bool> isCurrent, CancellationToken token)
    {
        if (_feedLoaded && Messages.Count == 0 && !_feedIncludesTail)
        {
            return true;
        }

        var loaded = Messages.ToArray();
        var hasWindow = _feedLoaded && loaded.Length != 0;
        var floor = hasWindow && _olderHistory.HasMore ? Position(loaded[0]) : (DateTimeOffset.MinValue, 0L);
        var ceiling = hasWindow ? (loaded[^1].SentAt, loaded[^1].Id) : (DateTimeOffset.MaxValue, long.MaxValue);
        var followTail = _feedIncludesTail;
        var firstUnread = !_feedLoaded && chat.UnreadCount > 0 ? chat.FirstUnreadMessageId : 0;
        var key = chat.IsPrivate ? await GetPrivateChatKeyAsync(chat).WaitAsync(token) : null;
        if (!isCurrent() || (chat.IsPrivate && key is null))
        {
            return true;
        }

        var (error, page) = await GetHistoryPageAsync(chat, key, firstUnread, SnapshotPageSize,
            firstUnread > 0 ? SnapshotPageSize : 0, token);
        if (firstUnread > 0 && IsMissingAnchor(error) && isCurrent())
        {
            // Сохранённая граница непрочитанного могла исчезнуть вместе с сообщением.
            (error, page) = await GetHistoryPageAsync(chat, key, 0, SnapshotPageSize, 0, token);
            firstUnread = 0;
        }

        if (!isCurrent())
        {
            return true;
        }

        if (!error.IsSuccess && (firstUnread != 0 || error.ErrorCode != 1))
        {
            return false;
        }

        var latest = firstUnread == 0 ? page.MaxBy(Position) : null;
        var snapshot = page.ToDictionary(row => row.Id);
        var coversAll = firstUnread == 0 && page.Count < SnapshotPageSize;
        var lower = coversAll ? (DateTimeOffset.MinValue, 0L) : page.Count == 0 ? ceiling : page.Min(Position);
        var upper = firstUnread == 0 || page.Count == 0 ? (DateTimeOffset.MaxValue, long.MaxValue) : page.Max(Position);
        var covered = loaded.Where(item => (item.SentAt, item.Id).CompareTo(lower) >= 0 &&
            (item.SentAt, item.Id).CompareTo(upper) <= 0).Select(item => item.Id).ToHashSet();
        var tailConnected = coversAll || loaded.Length == 0 || lower.CompareTo(ceiling) <= 0;

        var tailStart = page.Count == 0 ? ceiling : page.Min(Position);
        // Проверяем ближайшие к краям якоря и несколько соседей на страницу.
        var anchors = loaded.Reverse().Where((_, index) =>
            index % SnapshotPageSize < AnchorProbesPerPage || index >= loaded.Length - AnchorProbesPerPage);
        foreach (var anchor in anchors)
        {
            if (!hasWindow || covered.Contains(anchor.Id))
            {
                continue;
            }

            var (windowError, window) = await GetHistoryPageAsync(chat, key, anchor.Id, SnapshotPageSize, SnapshotPageSize, token);
            if (!isCurrent())
            {
                return true;
            }

            if (!windowError.IsSuccess && !IsMissingAnchor(windowError))
            {
                return false;
            }
            var reference = window.Find(row => row.Id == anchor.Id);
            if (!windowError.IsSuccess || reference is null)
            {
                continue;
            }

            var position = Position(reference);
            var start = window.Count(row => Position(row).CompareTo(position) < 0) < SnapshotPageSize
                ? (DateTimeOffset.MinValue, 0L) : window.Min(Position);
            var end = window.Count(row => Position(row).CompareTo(position) > 0) < SnapshotPageSize
                ? (DateTimeOffset.MaxValue, long.MaxValue) : window.Max(Position);
            covered.UnionWith(loaded.Where(item => (item.SentAt, item.Id).CompareTo(start) >= 0 &&
                (item.SentAt, item.Id).CompareTo(end) <= 0).Select(item => item.Id));
            var connectsTail = firstUnread == 0 && followTail && start.CompareTo(ceiling) <= 0 &&
                end.CompareTo(tailStart) >= 0;
            tailConnected |= connectsTail;
            foreach (var row in window)
            {
                if (Position(row).CompareTo(ceiling) <= 0 || connectsTail)
                {
                    snapshot[row.Id] = row;
                }
            }
        }

        // Если в окне не осталось якорей, короткая страница перед хвостом
        // всё ещё может подтвердить, что сервер вернул историю целиком.
        if (hasWindow && covered.Count == 0 && followTail && page.MinBy(Position) is { } boundary)
        {
            var (bridgeError, bridge) = await GetHistoryPageAsync(chat, key, boundary.Id, SnapshotPageSize, 0, token);
            if (!isCurrent())
            {
                return true;
            }

            if (!bridgeError.IsSuccess && !IsMissingAnchor(bridgeError))
            {
                return false;
            }
            if (bridgeError.IsSuccess && bridge.Count <= SnapshotPageSize && bridge.Any(row => row.Id == boundary.Id))
            {
                tailConnected = true;
                foreach (var row in bridge)
                {
                    snapshot[row.Id] = row;
                }
                covered.UnionWith(loaded.Select(item => item.Id));
            }
        }

        var visible = snapshot.Values.Where(row => Position(row).CompareTo(floor) >= 0 &&
            (Position(row).CompareTo(ceiling) <= 0 || followTail && tailConnected));
        await PrepareHistoryAsync(session, chat, visible, revision, isCurrent, token, prepared =>
        {
            IsHistoryUpdating = true;
            try
            {
                var chatUnchanged = session.Journal.ChatRevision(chat.Id, chat.IsPrivate) <= revision;
                foreach (var existing in loaded.Where(item => covered.Contains(item.Id) && !snapshot.ContainsKey(item.Id)))
                {
                    if ((session.Journal.Find(chat.Id, chat.IsPrivate, existing.Id)?.Revision ?? 0) <= revision)
                    {
                        // The chat list carries the authoritative unread count.
                        RemoveMessage(chat, existing.Id, adjustUnread: false);
                        session.Journal.Record(new(chat.Id, chat.IsPrivate, existing.Id, MessageUpdateKind.Delete));
                    }
                }

                if (!chat.IsPrivate && chatUnchanged)
                {
                    if (latest?.Regular is { } message)
                    {
                        chat.SetLastMessage(latest.Id, message.Text, latest.SentAt, message.EditedAt?.ToDateTimeOffset());
                    }
                    else if (coversAll && Messages.Count == 0)
                    {
                        chat.SetLastMessage(0, string.Empty, null);
                        if (session.Journal.UnreadRevision(chat.Id, chat.IsPrivate) <= revision)
                        {
                            chat.UnreadCount = 0;
                            chat.FirstUnreadMessageId = 0;
                        }
                    }
                }

                ApplyHistoryRows(session, chat, prepared, id => WasPrivateHistoryRead(chat, id));
                if (!_feedLoaded)
                {
                    SetHistoryEdges(page, firstUnread);
                    CompleteFeedLoad(firstUnread);
                }
                else if (followTail)
                {
                    _feedIncludesTail = tailConnected;
                }

                if (reconnectFeed is not null && ReferenceEquals(_reconnectFeed, reconnectFeed))
                {
                    _reconnectFeed = null;
                    reconnectFeed.TrySetResult();
                }
            }
            finally
            {
                IsHistoryUpdating = false;
            }
        });

        // Неподтверждённые строки остаются до следующего повода для сверки.
        return true;
    }

    private static (DateTimeOffset, long) Position(HistoryRow row) => (row.SentAt, row.Id);

    private sealed record HistoryRow(long Id, DateTimeOffset SentAt, MessageModel? Regular, PrivateMessageModel? Private)
    {
        internal bool IsDeleted => Private?.IsDeleted == true;
    }

    private async Task<(ErrorReturner Error, List<HistoryRow> Rows)> GetHistoryPageAsync(
        ChatItemViewModel chat, byte[]? key, long anchor, int before, int after, CancellationToken token = default)
    {
        if (key is not null)
        {
            var (error, messages) = await _messenger.GetPrivateMessagesAsync(chat.Id, key, anchor, before, after, token).WaitAsync(token);
            return (error, messages?.Select(message => new HistoryRow(message.MessageId, message.SentAt.ToDateTimeOffset(), null, message)).ToList() ?? []);
        }

        var (result, regular) = await _messenger.GetMessagesAsync(chat.Id, anchor, before, after, token).WaitAsync(token);
        return (result, regular?.Select(message => new HistoryRow(message.MessageId, message.SentAt.ToDateTimeOffset(), message, null)).ToList() ?? []);
    }

    private sealed record PreparedHistoryRow(HistoryRow Row, MessageItemViewModel? Item, bool Deleted,
        Infrastructure.Realtime.MessageUpdateJournal.Change? Change, bool RefreshQuote = false);

    private async Task PrepareHistoryAsync(RealtimeSession session, ChatItemViewModel chat,
        IEnumerable<HistoryRow> rows, long revision, Func<bool> isCurrent, CancellationToken token,
        Action<PreparedHistoryRow[]> apply, byte[]? key = null,
        ((DateTimeOffset, long) Start, (DateTimeOffset, long) End)? updateRange = null)
    {
        const int parallelRows = 8;
        var candidates = rows.ToDictionary(row => row.Id);
        var prepared = new Dictionary<long, PreparedHistoryRow>();
        while (isCurrent())
        {
            if (updateRange is { } range)
            {
                foreach (var update in session.Journal.NewMessagesSince(chat.Id, chat.IsPrivate, revision))
                {
                    var sentAt = (update.Message?.SentAt ?? update.Encrypted!.SentAt).ToDateTimeOffset();
                    var position = (sentAt, update.MessageId);
                    if (position.CompareTo(range.Start) >= 0 && position.CompareTo(range.End) <= 0 &&
                        !candidates.ContainsKey(update.MessageId))
                    {
                        candidates.Add(update.MessageId, new(update.MessageId, sentAt, update.Message,
                            key is null ? null : WebApiClient.DecryptPrivateMessage(update.Encrypted!, key)));
                    }
                }
            }

            var pending = candidates.Values.Where(row => !prepared.TryGetValue(row.Id, out var result) ||
                result.Change != session.Journal.Find(chat.Id, chat.IsPrivate, row.Id)).ToArray();
            if (pending.Length == 0)
            {
                apply(prepared.Values.OrderBy(result => Position(result.Row)).ToArray());
                return;
            }

            foreach (var batch in pending.Chunk(parallelRows))
            {
                var items = await Task.WhenAll(batch.Select(PrepareRowAsync)).WaitAsync(token);
                if (!isCurrent())
                {
                    return;
                }
                foreach (var item in items)
                {
                    prepared[item.Row.Id] = item;
                }
            }
        }

        async Task<PreparedHistoryRow> PrepareRowAsync(HistoryRow row)
        {
            var change = session.Journal.Find(chat.Id, chat.IsPrivate, row.Id);
            if (change?.Kind == MessageUpdateKind.Delete || row.IsDeleted && (change?.Revision ?? 0) <= revision)
            {
                return new(row, null, true, change);
            }

            var editTime = row.Regular?.EditedAt ?? row.Private?.EditedAt;
            var preferUpdate = change?.Payload is not null && (change.Revision > revision ||
                change.EditTime is { } editedAt && editedAt.CompareTo(editTime) > 0);
            var current = Messages.FirstOrDefault(message => message.Id == row.Id);
            var isEdited = row.Regular?.IsEdited ?? row.Private!.IsEdited;
            if (current is not null)
            {
                if (preferUpdate && current.AppliedUpdateRevision == change!.Revision ||
                    !preferUpdate && (current.EditedAt > editTime?.ToDateTimeOffset() || current.IsEdited && !isEdited))
                {
                    return new(row, current, false, change);
                }
                if (!preferUpdate && current.EditedAt == editTime?.ToDateTimeOffset() && current.IsEdited == isEdited &&
                    current.Text == (row.Regular?.Text ?? PrivateMessageText(row.Private!)))
                {
                    return new(row, current, false, change, RefreshQuote: true);
                }
            }

            var item = preferUpdate
                ? await BuildUpdatedItemAsync(session, chat, change!.Payload!)
                : row.Regular is { } regular
                    ? await CreateMessageItemAsync(regular, session.UserId)
                    : CreatePrivateMessageItem(row.Private!, session.UserId);
            if (item is not null && preferUpdate)
            {
                item.AppliedUpdateRevision = change!.Revision;
            }
            return new(row, item, false, change);
        }
    }

    private void ApplyHistoryRows(RealtimeSession session, ChatItemViewModel chat, PreparedHistoryRow[] rows,
        Func<long, bool> markRead)
    {
        foreach (var (row, item, deleted, change, refreshQuote) in rows)
        {
            if (deleted)
            {
                if (change?.Kind != MessageUpdateKind.Delete)
                {
                    RemoveMessage(chat, row.Id, adjustUnread: false);
                    session.Journal.Record(new(chat.Id, chat.IsPrivate, row.Id, MessageUpdateKind.Delete));
                }
                continue;
            }
            if (item is null)
            {
                continue;
            }

            var existing = Messages.FirstOrDefault(message => message.Id == row.Id);
            if (!ReferenceEquals(item, existing))
            {
                UpsertMessage(item, allowInsert: true);
            }
            var visible = existing ?? item;
            if (row.Regular is { } regular)
            {
                foreach (var reader in regular.ReadBy)
                {
                    visible.RegisterReader(reader);
                }
                if (refreshQuote && ReferenceEquals(item, existing))
                {
                    UpdateSnapshotQuote(item, CreateReplyContent(regular.ReplyTo));
                }
            }
            if (markRead(row.Id))
            {
                visible.MarkReadByCurrentUser();
            }
            if (change is not null)
            {
                change.WaitingForNew = false;
            }
            if (item.IsEdited)
            {
                UpdateQuotes(item.Id, visible.Text);
            }
        }
    }

    private async Task MergeSentRowAsync(RealtimeSession session, ChatItemViewModel chat, int version,
        HistoryRow row, long revision)
    {
        while (IsOpen(session, chat, version) && _feedIncludesTail)
        {
            var window = _historyWindowLifetime.Token;
            try
            {
                await PrepareHistoryAsync(session, chat, [row], revision,
                    () => IsOpenWindow(session, chat, version, window) && _feedIncludesTail,
                    window, rows => ApplyHistoryRows(session, chat, rows, _ => false));
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested)
            {
            }
            if (!window.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
