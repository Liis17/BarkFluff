using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private bool _historyFromCache;

    private static bool CanCacheHistory(ChatItemViewModel chat) =>
        chat.Definition.ChatType is ChatType.Regular or ChatType.Private;

    private long? HistoryCacheGeneration(RealtimeSession session, ChatItemViewModel chat) =>
        CanCacheHistory(chat) ? _chatCache?.History?.Generation(CacheScope(session)) : null;

    private void SaveHistoryPage(RealtimeSession session, ChatItemViewModel chat, long? generation,
        IEnumerable<HistoryRow> rows, long revision, ((DateTimeOffset, long), (DateTimeOffset, long)) range)
    {
        if (generation is not { } epoch || HistoryCacheGeneration(session, chat) != epoch)
            return;

        var messages = new Dictionary<long, CachedHistoryMessage>();
        foreach (var row in rows)
        {
            var change = session.Journal.Find(chat.Id, chat.IsPrivate, row.Id);
            if (row.IsDeleted || change?.Kind == MessageUpdateKind.Delete)
                continue;

            var cached = change?.Payload is { } payload && (change.Revision > revision ||
                change.EditTime is { } editedAt && editedAt.CompareTo(row.Regular?.EditedAt ?? row.Private?.EditedAt) > 0)
                ? CacheMessage(payload)
                : new CachedHistoryMessage(row.Id, row.SentAt, row.Regular, row.Private?.EncryptedSource);
            if (cached is { Regular: not null } or { Encrypted: not null })
                messages[cached.Id] = cached;
        }

        foreach (var update in session.Journal.NewMessagesSince(chat.Id, chat.IsPrivate, revision))
        {
            if (CacheMessage(update) is { } row && (row.SentAt, row.Id).CompareTo(range.Item1) >= 0 &&
                (row.SentAt, row.Id).CompareTo(range.Item2) <= 0)
                messages.TryAdd(row.Id, row);
        }

        // Модель приватного сообщения без конверта не подтверждает записываемый диапазон.
        if (chat.IsPrivate && rows.Any(row => !row.IsDeleted && row.Private?.EncryptedSource is null))
            return;

        _chatCache!.History!.Schedule(CacheScope(session), epoch, new(chat.Id, messages.Values.ToArray(), range));
        session.CanWriteHistoryEvents = true;
        if (range.Item2 == (DateTimeOffset.MaxValue, long.MaxValue))
            session.ConfirmedHistoryTails.Add(chat.Id);
    }

    private static CachedHistoryMessage? CacheMessage(MessageUpdate update)
    {
        var time = (update.Message?.SentAt ?? update.Encrypted?.SentAt)?.ToDateTimeOffset();
        return time is { } sentAt
            ? new(update.MessageId, sentAt, update.IsPrivate ? null : update.Message, update.Encrypted)
            : null;
    }

    private void SaveHistoryEvent(RealtimeSession session, ChatItemViewModel? chat, MessageUpdate update,
        MessageUpdate? effective)
    {
        if (!session.CanWriteHistoryEvents || chat is null || HistoryCacheGeneration(session, chat) is not { } epoch)
            return;
        if (update.Kind == MessageUpdateKind.Delete)
        {
            _chatCache!.History!.Schedule(CacheScope(session), epoch, new(chat.Id, [], DeleteId: update.MessageId));
        }
        else if (effective is not null && CacheMessage(effective) is { } row)
        {
            _chatCache!.History!.Schedule(CacheScope(session), epoch, new(chat.Id, [row],
                Append: update.Kind == MessageUpdateKind.New && session.ConfirmedHistoryTails.Contains(chat.Id)));
        }
    }

    private async Task<bool> ShowCachedHistoryAsync(RealtimeSession session, ChatItemViewModel chat,
        byte[]? key, long anchor, Func<bool> isCurrent, CancellationToken token, bool older = false, bool replace = false)
    {
        if (HistoryCacheGeneration(session, chat) is not { } epoch)
            return false;
        var revision = session.Journal.Revision;
        var pinRevision = _pinnedRevision;
        var scope = CacheScope(session);
        var page = await _chatCache!.History!.ReadAsync(scope, chat.Id, chat.IsPrivate, anchor, SnapshotPageSize, older)
            .WaitAsync(token);
        bool IsCurrent() => isCurrent() && _chatCache.History.Generation(scope) == epoch;
        if (!IsCurrent())
            return false;

        var messages = older ? page.Messages.SkipLast(1) : page.Messages;
        var rows = messages.Select(row => new HistoryRow(row.Id, row.SentAt,
            row.Regular, row.Encrypted is { } encrypted && key is not null
                ? WebApiClient.DecryptPrivateMessage(encrypted, key) : null)).ToList();
        if (rows.Count == 0)
        {
            if (older)
                _olderHistory.HasMore = false;
            return false;
        }

        var applied = false;
        await PrepareHistoryAsync(session, chat, rows, revision, IsCurrent, token, prepared =>
        {
            if (!older && anchor > 0 && !prepared.Any(row => row.Row.Id == anchor && row.Item is not null))
                return;
            IsHistoryUpdating = true;
            try
            {
                if (!older)
                {
                    if (replace)
                    {
                        ResetHistoryWindow();
                        CancelPendingReadBatch();
                    }
                    RemoveRowsOutside(prepared);
                    _feedIncludesTail = page.IncludesTail;
                    _newerHistory.Cursor = rows[^1].Id;
                }
                _olderHistory.Cursor = rows[0].Id;
                // Онлайн сервер может располагать более ранними страницами, чем локальный кэш.
                _olderHistory.HasMore = page.HasOlder || !IsOffline;
                ApplyHistoryRows(session, chat, prepared, id => WasPrivateHistoryRead(chat, id), cached: true);
                if (page.Pins is { } pins && pinRevision == _pinnedRevision)
                {
                    _pinnedMessages.Clear();
                    _pinnedMessages.AddRange(pins.Where(pin =>
                        session.Journal.Find(chat.Id, false, pin.Message.Id)?.Kind != MessageUpdateKind.Delete));
                    ApplyPinnedState();
                    RefreshPinnedBar();
                }
                _historyFromCache = true;
                if (older)
                    TrimHistoryWindow(loadedOlder: true);
                else
                    CompleteFeedLoad(anchor);
                applied = true;
            }
            finally
            {
                IsHistoryUpdating = false;
            }
        }, key, cached: true);
        return applied;
    }

    private void SaveHistoryPins(RealtimeSession session, ChatItemViewModel chat, long? generation)
    {
        if (generation is { } epoch && chat.Definition.ChatType == ChatType.Regular)
            _chatCache!.History!.Schedule(CacheScope(session), epoch,
                new(chat.Id, [], Pins: _pinnedMessages.Take(HistoryWindowLimit).Select(pin => pin.Clone()).ToArray()));
    }

    private void RemoveRowsOutside(PreparedHistoryRow[] rows)
    {
        var visible = rows.Where(row => row.Item is not null).Select(row => row.Row.Id).ToHashSet();
        foreach (var item in Messages.Where(item => !visible.Contains(item.Id)).ToArray())
            Messages.Remove(item);
    }

    private void ApplyNetworkHistory(RealtimeSession session, ChatItemViewModel chat, List<HistoryRow> rows,
        PreparedHistoryRow[] prepared, long anchor)
    {
        var replacingCache = _historyFromCache;
        var preserveScroll = replacingCache && prepared.Any(row => row.Item is not null &&
            Messages.Any(item => item.Id == row.Row.Id));
        IsHistoryUpdating = true;
        try
        {
            if (replacingCache)
            {
                CancelPendingReadBatch();
                RemoveRowsOutside(prepared);
            }
            SetHistoryEdges(rows, anchor);
            ApplyHistoryRows(session, chat, prepared, id => chat.IsPrivate && (anchor == 0 || id < anchor));
            _historyFromCache = false;
            if (preserveScroll)
                _feedLoaded = true;
            else
                CompleteFeedLoad(anchor);
        }
        finally
        {
            IsHistoryUpdating = false;
        }
    }
}

