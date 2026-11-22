using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.WebApi.Core;
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
        var version = _messageLoadVersion;
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

        var historyTask = ResyncHistoryAsync(session, chat, version, revision, reconnectFeed, token);
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

    private async Task<bool> ResyncHistoryAsync(RealtimeSession session, ChatItemViewModel chat, int version,
        long revision, TaskCompletionSource? reconnectFeed, CancellationToken token)
    {
        var loaded = Messages.OrderBy(item => (item.SentAt, item.Id)).ToArray();
        var hasWindow = _feedLoaded && loaded.Length != 0;
        var floor = hasWindow ? (loaded[0].SentAt, loaded[0].Id) : (DateTimeOffset.MinValue, 0L);
        var ceiling = hasWindow ? (loaded[^1].SentAt, loaded[^1].Id) : (DateTimeOffset.MaxValue, long.MaxValue);
        var followTail = _feedIncludesTail;
        var firstUnread = !_feedLoaded && chat.UnreadCount > 0 ? chat.FirstUnreadMessageId : 0;
        var key = chat.IsPrivate ? await GetPrivateChatKeyAsync(chat) : null;
        if (!IsOpen(session, chat, version) || (chat.IsPrivate && key is null))
        {
            return true;
        }

        var (error, page) = await GetHistoryPageAsync(chat, key, firstUnread, SnapshotPageSize,
            firstUnread > 0 ? SnapshotPageSize : 0, token);
        if (firstUnread > 0 && !error.IsSuccess && IsOpen(session, chat, version))
        {
            // Сохранённая граница непрочитанного могла исчезнуть вместе с сообщением.
            (error, page) = await GetHistoryPageAsync(chat, key, 0, SnapshotPageSize, 0, token);
            firstUnread = 0;
        }

        if (!IsOpen(session, chat, version))
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
        var failedAnchors = 0;
        var checkedTail = false;
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
            if (!IsOpen(session, chat, version))
            {
                return true;
            }

            // Ошибка якоря может означать удаление или потерю связи.
            var reference = window.Find(row => row.Id == anchor.Id);
            if (!windowError.IsSuccess || reference is null)
            {
                if (++failedAnchors == AnchorProbesPerPage && !checkedTail)
                {
                    var reachable = await TailReachableAsync();
                    if (!IsOpen(session, chat, version) || !reachable)
                    {
                        return !IsOpen(session, chat, version);
                    }
                    checkedTail = true;
                }
                continue;
            }

            failedAnchors = 0;
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
            if (!IsOpen(session, chat, version))
            {
                return true;
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
            failedAnchors = bridgeError.IsSuccess ? 0 : 1;
        }

        if (failedAnchors > 0)
        {
            var reachable = await TailReachableAsync();
            if (!IsOpen(session, chat, version) || !reachable)
            {
                return !IsOpen(session, chat, version);
            }
        }

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

        foreach (var row in snapshot.Values.OrderBy(row => row.Id))
        {
            if (Position(row).CompareTo(floor) >= 0 &&
                (Position(row).CompareTo(ceiling) <= 0 || followTail && tailConnected))
            {
                await MergeHistoryRowAsync(session, chat, version, row, revision,
                    markRead: chat.IsPrivate && (chat.UnreadCount == 0 || row.Id < chat.FirstUnreadMessageId));
            }
        }

        if (IsOpen(session, chat, version))
        {
            _feedIncludesTail = firstUnread == 0
                ? followTail && tailConnected
                : page.Count(row => row.Id > firstUnread) < SnapshotPageSize;
            if (!_feedLoaded)
            {
                CompleteFeedLoad(firstUnread);
            }

            if (reconnectFeed is not null && ReferenceEquals(_reconnectFeed, reconnectFeed))
            {
                _reconnectFeed = null;
                reconnectFeed.TrySetResult();
            }
        }

        // Неподтверждённые строки остаются до следующего повода для сверки.
        return true;

        async Task<bool> TailReachableAsync()
        {
            var (tailError, _) = await GetHistoryPageAsync(chat, key, 0, SnapshotPageSize, 0, token);
            return tailError.IsSuccess || tailError.ErrorCode == 1;
        }
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

    private async Task MergeHistoryRowAsync(RealtimeSession session, ChatItemViewModel chat, int version,
        HistoryRow row, long revision, bool markRead)
    {
        while (IsOpen(session, chat, version))
        {
            var change = session.Journal.Find(chat.Id, chat.IsPrivate, row.Id);
            if (change?.Kind == MessageUpdateKind.Delete)
            {
                return;
            }

            if (row.IsDeleted && (change is null || change.Revision <= revision))
            {
                RemoveMessage(chat, row.Id, adjustUnread: false);
                session.Journal.Record(new(chat.Id, chat.IsPrivate, row.Id, MessageUpdateKind.Delete));
                return;
            }

            var editTime = row.Regular?.EditedAt ?? row.Private?.EditedAt;
            var current = Messages.FirstOrDefault(message => message.Id == row.Id);
            var preferUpdate = change?.Payload is not null && (change.Revision > revision ||
                change.EditTime is { } editedAt && editedAt.CompareTo(editTime) > 0);
            var useUpdate = preferUpdate && current?.AppliedUpdateRevision != change!.Revision;
            if (!useUpdate && current is not null)
            {
                if (row.Regular is { } data)
                {
                    foreach (var reader in data.ReadBy)
                    {
                        current.RegisterReader(reader);
                    }
                }
                if (markRead)
                {
                    current.MarkReadByCurrentUser();
                }

                var isEdited = row.Regular?.IsEdited ?? row.Private!.IsEdited;
                if (preferUpdate || current.EditedAt > editTime?.ToDateTimeOffset() || current.IsEdited && !isEdited)
                {
                    return;
                }

                // Вложения меняются вместе с версией сообщения. Цитата резолвится сервером
                // независимо от версии ответа, поэтому её сравниваем отдельно.
                if (current.EditedAt == editTime?.ToDateTimeOffset() && current.IsEdited == isEdited &&
                    current.Text == (row.Regular?.Text ?? PrivateMessageText(row.Private!)))
                {
                    if (row.Regular is { } regularRow)
                    {
                        UpdateSnapshotQuote(current, CreateReplyContent(regularRow.ReplyTo));
                    }
                    return;
                }
            }
            var item = useUpdate
                ? await BuildUpdatedItemAsync(session, chat, change!.Payload!)
                : row.Regular is { } regular
                    ? await CreateMessageItemAsync(regular, session.UserId)
                    : CreatePrivateMessageItem(row.Private!, session.UserId);
            if (!IsOpen(session, chat, version))
            {
                return;
            }

            if (change != session.Journal.Find(chat.Id, chat.IsPrivate, row.Id))
            {
                continue;
            }

            if (item is not null)
            {
                if (markRead)
                {
                    item.MarkReadByCurrentUser();
                }

                if (useUpdate)
                {
                    item.AppliedUpdateRevision = change!.Revision;
                }
                UpsertMessage(item, allowInsert: true);
                if (change is not null)
                {
                    change.WaitingForNew = false;
                }
                if (item.IsEdited)
                {
                    var original = Messages.First(message => message.Id == item.Id);
                    UpdateQuotes(item.Id, original.Text);
                }
            }
            return;
        }
    }
}
