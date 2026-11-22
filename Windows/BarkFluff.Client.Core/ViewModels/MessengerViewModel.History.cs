using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.WebApi.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private sealed class HistoryEdge
    {
        internal long Cursor;
        internal bool HasMore;
        internal bool Loading;
        internal bool Failed;
    }

    private const int HistoryWindowLimit = 1000;
    private HistoryEdge _olderHistory = new();
    private HistoryEdge _newerHistory = new();
    private bool _tailReloadFailed;
    private int _historyReplacement;
    private bool _isHistoryResyncing;
    private CancellationTokenSource _historyWindowLifetime = new();

    [ObservableProperty] private bool _isHistoryUpdating;
    [ObservableProperty] private bool _hasHistoryError;

    private void ResetHistoryWindow()
    {
        _historyWindowLifetime.Cancel();
        _historyWindowLifetime.Dispose();
        _historyWindowLifetime = new();
        _isHistoryResyncing = false;
        _olderHistory = new();
        _newerHistory = new();
        IsHistoryUpdating = false;
        _tailReloadFailed = false;
        HasHistoryError = false;
    }

    private void SetHistoryEdges(List<HistoryRow> rows, long anchor)
    {
        var reference = rows.Find(row => row.Id == anchor);
        _olderHistory.Cursor = rows.MinBy(Position)?.Id ?? 0;
        _newerHistory.Cursor = rows.MaxBy(Position)?.Id ?? 0;
        _olderHistory.HasMore = reference is null ? rows.Count >= SnapshotPageSize :
            rows.Count(row => Position(row).CompareTo(Position(reference)) < 0) >= SnapshotPageSize;
        _feedIncludesTail = reference is null || !HasNewerHistory(rows, reference);
    }

    private static bool HasNewerHistory(List<HistoryRow> rows, HistoryRow reference) =>
        rows.Count(row => Position(row).CompareTo(Position(reference)) > 0) >= SnapshotPageSize;

    private void UpdateHistoryError() => HasHistoryError = _olderHistory.Failed || _newerHistory.Failed || _tailReloadFailed;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadOlderHistoryAsync() => LoadHistoryPageAsync(older: true, retry: false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadNewerHistoryAsync() => LoadHistoryPageAsync(older: false, retry: false);

    [RelayCommand]
    private async Task RetryHistoryAsync()
    {
        var older = _olderHistory;
        var newer = _newerHistory;
        if (_tailReloadFailed)
        {
            await ReloadHistoryTailAsync();
        }

        await Task.WhenAll(
            older == _olderHistory && older.Failed ? LoadHistoryPageAsync(older: true, retry: true) : Task.CompletedTask,
            newer == _newerHistory && newer.Failed ? LoadHistoryPageAsync(older: false, retry: true) : Task.CompletedTask);
    }

    private async Task LoadHistoryPageAsync(bool older, bool retry)
    {
        var edge = older ? _olderHistory : _newerHistory;
        var session = _realtimeSession;
        var chat = SelectedChat;
        var version = _chatVersion;
        if (!_feedLoaded || edge.Loading || edge.Failed && !retry ||
            (older ? !edge.HasMore : _feedIncludesTail) || chat is null || !IsOpen(session, chat, version))
        {
            return;
        }

        edge.Loading = true;
        edge.Failed = false;
        UpdateHistoryError();
        var window = _historyWindowLifetime.Token;
        bool IsCurrentWindow() => IsOpenWindow(session, chat, version, window);
        try
        {
            using var reading = session!.Journal.ReadHistory(chat.Id, chat.IsPrivate);
            var revision = session.Journal.Revision;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token, window);
            var key = chat.IsPrivate ? await GetPrivateChatKeyAsync(chat).WaitAsync(lifetime.Token) : null;
            if (!IsCurrentWindow())
            {
                return;
            }

            var ordered = older ? Messages.AsEnumerable() : Messages.AsEnumerable().Reverse();
            var candidates = ordered.Select(item => item.Id).Prepend(edge.Cursor).Where(id => id > 0)
                .Distinct().Take(AnchorProbesPerPage).ToArray();
            foreach (var cursor in chat.IsPrivate && key is null ? [] : candidates)
            {
                var (error, rows) = await GetHistoryPageAsync(chat, key, cursor,
                    older ? SnapshotPageSize : 0, older ? 0 : SnapshotPageSize, lifetime.Token);
                if (!IsCurrentWindow())
                {
                    return;
                }
                if (!error.IsSuccess && !IsMissingAnchor(error))
                {
                    break;
                }

                var reference = rows.Find(row => row.Id == cursor);
                if (!error.IsSuccess || reference is null)
                {
                    continue;
                }

                // Приватный tombstone остаётся допустимым курсором, даже если строка скрыта.
                var page = rows.Where(row => older ? Position(row).CompareTo(Position(reference)) < 0 :
                    Position(row).CompareTo(Position(reference)) > 0).OrderBy(Position).ToArray();
                var atEnd = page.Length < SnapshotPageSize;
                var range = (older && atEnd ? (DateTimeOffset.MinValue, 0L) : rows.Min(Position),
                    !older && atEnd ? (DateTimeOffset.MaxValue, long.MaxValue) : rows.Max(Position));

                await PrepareHistoryAsync(session, chat, page, revision, IsCurrentWindow, lifetime.Token, prepared =>
                {
                    IsHistoryUpdating = true;
                    try
                    {
                        ApplyHistoryRows(session, chat, prepared, id => WasPrivateHistoryRead(chat, id));
                        edge.Cursor = (older ? page.FirstOrDefault() : page.LastOrDefault())?.Id ?? cursor;
                        if (older)
                        {
                            edge.HasMore = !atEnd;
                        }
                        else if (atEnd)
                        {
                            _feedIncludesTail = true;
                        }
                        TrimHistoryWindow(older);
                    }
                    finally
                    {
                        IsHistoryUpdating = false;
                    }
                }, key, range);
                return;
            }

            edge.Failed = true;
        }
        catch (OperationCanceledException) when (!IsCurrentWindow())
        {
        }
        finally
        {
            if (IsCurrentWindow())
            {
                edge.Loading = false;
                UpdateHistoryError();
            }
        }
    }

    private void TrimHistoryWindow(bool loadedOlder)
    {
        // Дальний край можно сбросить после прокрутки. Параллельная страница или сверка
        // ещё могут использовать его как границу непрерывности.
        if (_isHistoryResyncing || (loadedOlder ? _newerHistory.Loading : _olderHistory.Loading) ||
            Messages.Count <= HistoryWindowLimit)
        {
            return;
        }

        while (Messages.Count > HistoryWindowLimit)
        {
            Messages.RemoveAt(loadedOlder ? Messages.Count - 1 : 0);
        }
        if (loadedOlder)
        {
            _feedIncludesTail = false;
            _newerHistory.Cursor = Messages[^1].Id;
            _reconnectFeed?.TrySetResult();
            _reconnectFeed = null;
        }
        else
        {
            _olderHistory.HasMore = true;
            _olderHistory.Cursor = Messages[0].Id;
        }
    }

    private bool WasPrivateHistoryRead(ChatItemViewModel chat, long id) =>
        chat.IsPrivate && (chat.UnreadCount == 0 || id < chat.FirstUnreadMessageId);

    private bool IsOpenWindow(RealtimeSession? session, ChatItemViewModel chat, int version, CancellationToken window) =>
        !window.IsCancellationRequested && IsOpen(session, chat, version);

    private bool IsInsideLoadedWindow(MessageUpdate update)
    {
        var sentAt = (update.Message?.SentAt ?? update.Encrypted!.SentAt).ToDateTimeOffset();
        var position = (sentAt, update.MessageId);
        return Messages.Count != 0 && position.CompareTo(Position(Messages[0])) >= 0 &&
            position.CompareTo(Position(Messages[^1])) <= 0;
    }

    private static bool IsMissingAnchor(ErrorReturner error) =>
        error.ErrorCode is 1 or ErrorReturner.HistoryAnchorNotFound;

    public async Task NavigateToMessageAsync(long messageId)
    {
        if (messageId <= 0)
        {
            _historyReplacement++;
            ActionError = _localization.GetString("Messenger_HistoryMessageUnavailable");
            return;
        }

        if (Messages.Any(message => message.Id == messageId))
        {
            _historyReplacement++;
            ActionError = null;
            RequestScroll(MessageScrollTarget.Message, messageId);
            return;
        }

        ActionError = null;
        await ReplaceHistoryWindowAsync(messageId);
    }

    private Task ReloadHistoryTailAsync() => ReplaceHistoryWindowAsync(0);

    private async Task ReplaceHistoryWindowAsync(long anchor)
    {
        var session = _realtimeSession;
        var chat = SelectedChat;
        var version = _chatVersion;
        if (chat is null || !IsOpen(session, chat, version))
        {
            return;
        }

        var window = _historyWindowLifetime.Token;
        var request = ++_historyReplacement;
        bool IsCurrentRequest() => request == _historyReplacement && IsOpenWindow(session, chat, version, window);
        try
        {
            using var reading = session!.Journal.ReadHistory(chat.Id, chat.IsPrivate);
            var revision = session.Journal.Revision;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token, window);
            var key = chat.IsPrivate ? await GetPrivateChatKeyAsync(chat).WaitAsync(lifetime.Token) : null;
            if (!IsCurrentRequest())
            {
                return;
            }
            if (chat.IsPrivate && key is null)
            {
                ShowReplacementError(missing: false);
                return;
            }

            var (error, rows) = await GetHistoryPageAsync(chat, key, anchor, SnapshotPageSize,
                anchor == 0 ? 0 : SnapshotPageSize, lifetime.Token);
            if (!IsCurrentRequest())
            {
                return;
            }
            if (!error.IsSuccess && (anchor != 0 || error.ErrorCode != 1))
            {
                ShowReplacementError(IsMissingAnchor(error));
                return;
            }
            if (anchor > 0 && rows.Find(row => row.Id == anchor) is not { IsDeleted: false })
            {
                ShowReplacementError(missing: true);
                return;
            }

            await PrepareHistoryAsync(session, chat, rows, revision, IsCurrentRequest, lifetime.Token, prepared =>
            {
                var visibleIds = prepared.Where(row => row.Item is not null).Select(row => row.Row.Id).ToHashSet();
                if (anchor > 0 && !visibleIds.Contains(anchor))
                {
                    ShowReplacementError(missing: true);
                    return;
                }

                ResetHistoryWindow();
                IsHistoryUpdating = true;
                try
                {
                    CancelPendingReadBatch();
                    SetHistoryEdges(rows, anchor);
                    foreach (var item in Messages.Where(item => !visibleIds.Contains(item.Id)).ToArray())
                    {
                        Messages.Remove(item);
                    }
                    ApplyHistoryRows(session, chat, prepared, id => WasPrivateHistoryRead(chat, id));
                    var refreshPins = !_feedLoaded && !chat.IsPrivate;
                    _feedLoaded = true;
                    _reconnectFeed?.TrySetResult();
                    _reconnectFeed = null;
                    RequestScroll(anchor == 0 ? MessageScrollTarget.Bottom : MessageScrollTarget.Message, anchor == 0 ? null : anchor);
                    if (refreshPins)
                    {
                        _ = RefreshPinsAsync(chat, version, reportError: false);
                    }
                }
                finally
                {
                    IsHistoryUpdating = false;
                }
            }, key, HistoryRange(rows, anchor));
        }
        catch (OperationCanceledException) when (!IsCurrentRequest())
        {
        }

        void ShowReplacementError(bool missing)
        {
            if (anchor > 0)
            {
                ActionError = _localization.GetString(missing
                    ? "Messenger_HistoryMessageUnavailable" : "Messenger_HistoryLoadFailed");
            }
            else
            {
                _tailReloadFailed = true;
                UpdateHistoryError();
            }
        }
    }

    private static ((DateTimeOffset, long), (DateTimeOffset, long)) HistoryRange(List<HistoryRow> rows, long anchor)
    {
        var reference = rows.Find(row => row.Id == anchor);
        var hasOlder = reference is null ? rows.Count >= SnapshotPageSize :
            rows.Count(row => Position(row).CompareTo(Position(reference)) < 0) >= SnapshotPageSize;
        return (hasOlder ? rows.Min(Position) : (DateTimeOffset.MinValue, 0L),
            reference is not null && HasNewerHistory(rows, reference) ? rows.Max(Position) : (DateTimeOffset.MaxValue, long.MaxValue));
    }

    private static (DateTimeOffset, long) Position(MessageItemViewModel item) => (item.SentAt, item.Id);
}
