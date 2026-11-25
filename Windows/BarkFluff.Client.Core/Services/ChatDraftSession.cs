using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Exceptions.Messages;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services;

// Состояние сессии меняется только в UI-потоке. В каждом чате одновременно выполняется один RPC.
internal sealed class ChatDraftSession(
    string node, long userId, IChatDraftStore store, IChatDraftRemote remote,
    IUiDispatcher ui, TimeProvider clock, CancellationToken lifetime, Func<bool> isCurrent,
    Action<string, bool> changed, Task previousWrites)
{
    private enum Outcome { Retry, Read, Written, Deleted, Conflict, Unsupported, Rejected, MissingReply }

    private sealed class Entry(ChatDraft draft)
    {
        public ChatDraft Draft = draft;
        public bool Known;
        public bool Open;
        public long Generation;
        public bool Busy;
        public bool Due;
        public bool ResolveConflict;
        public bool RetriedDelete;
        public int RetrySeconds = 2;
        public ITimer? Timer;
        public int ScheduleVersion;
    }

    private readonly Dictionary<string, Entry> _entries = store.Load(node, userId)
        .ToDictionary(pair => pair.Key, pair => new Entry(pair.Value) { Known = true });
    private Dictionary<string, ChatDraft> _dirty = [];
    private Task _writes = previousWrites;
    private ITimer? _writeTimer;
    private int _writeVersion;
    private bool _stopped;
    private bool _unsupported;

    public ChatDraft? Get(string chatId) =>
        _entries.TryGetValue(chatId, out var entry) && entry.Known ? entry.Draft : null;

    public void Resume()
    {
        foreach (var (id, entry) in _entries.ToArray())
            if (entry.Draft.Pending)
                Schedule(id, entry, 0);
    }

    public Task Stop()
    {
        _stopped = true;
        StopRemoteTimers();
        return Flush();
    }

    private void StopRemoteTimers()
    {
        foreach (var entry in _entries.Values)
            entry.Timer?.Dispose();
    }

    public void Open(string id, ChatType type)
    {
        var entry = GetEntry(id, type);
        entry.Open = true;
        if (type == ChatType.Regular && !entry.Draft.LocalOnly && !entry.Busy && (!entry.Draft.Pending || entry.Timer is null))
            Schedule(id, entry, 0);
    }

    public void Leave(string id)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return;

        entry.Open = false;
        if (entry.Draft.Pending)
            Schedule(id, entry, 0);
        else
        {
            entry.Timer?.Dispose();
            entry.Timer = null;
            entry.ScheduleVersion++;
        }
        _ = Flush();
    }

    public void Set(string id, ChatType type, string text, long replyId)
    {
        var entry = GetEntry(id, type);
        var previous = entry.Draft;
        if (previous.Deleted && string.IsNullOrWhiteSpace(text) && replyId == 0 ||
            !previous.Deleted && previous.Text == text && previous.ReplyId == replyId)
            return;

        var next = new ChatDraft(text, replyId, type, previous.Revision, type == ChatType.Regular);
        if (!next.HasContent && previous.HasContent)
            next = next with { Text = previous.Text, ReplyId = previous.ReplyId, Deleted = true };

        entry.Generation++;
        Update(id, entry, next, applyToComposer: false, deferWrite: true);
        entry.RetrySeconds = 2;
        entry.ResolveConflict = false;
        entry.RetriedDelete = false;
        if (next.Pending)
            Schedule(id, entry, 2);
    }

    public long Generation(string id) => _entries.GetValueOrDefault(id)?.Generation ?? 0;

    public bool ClearSent(string id, long generation, string text, long replyId)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Generation != generation)
            return false;

        var previous = entry.Draft;
        if (!previous.HasContent)
            return true;

        entry.Generation++;
        Update(id, entry, previous with
        {
            Text = text,
            ReplyId = replyId,
            Deleted = true,
            Pending = previous.Type == ChatType.Regular && !previous.LocalOnly
        }, applyToComposer: true);
        entry.ResolveConflict = false;
        entry.RetriedDelete = false;
        entry.RetrySeconds = 2;
        if (entry.Draft.Pending)
            Schedule(id, entry, 0);
        return true;
    }

    private Entry GetEntry(string id, ChatType type)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            entry = new Entry(new ChatDraft("", 0, type));
            _entries.Add(id, entry);
        }
        return entry;
    }

    private void Update(string id, Entry entry, ChatDraft draft, bool applyToComposer, bool deferWrite = false)
    {
        var previous = entry.Draft;
        entry.Draft = draft;
        entry.Known = true;
        if (previous != draft)
        {
            _dirty[id] = draft;
            if (!deferWrite)
                _ = Flush();
            else if (_writeTimer is null)
            {
                var version = ++_writeVersion;
                _writeTimer = clock.CreateTimer(_ => ui.Post(() =>
                {
                    if (version == _writeVersion)
                        _ = Flush();
                }), null,
                    TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }
        changed(id, applyToComposer);
    }

    public Task Flush()
    {
        _writeTimer?.Dispose();
        _writeTimer = null;
        _writeVersion++;
        if (_dirty.Count == 0)
            return _writes;

        var batch = _dirty;
        _dirty = [];
        var previous = _writes;
        return _writes = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            foreach (var (chatId, draft) in batch)
                store.Save(node, userId, chatId, draft);
        });
    }

    private void Schedule(string id, Entry entry, int seconds)
    {
        if (_stopped || _unsupported || !isCurrent())
            return;

        entry.Timer?.Dispose();
        entry.Timer = null;
        var version = ++entry.ScheduleVersion;
        entry.Due = seconds == 0;
        if (entry.Due)
        {
            RunIfDue(id, entry);
            return;
        }

        entry.Timer = clock.CreateTimer(_ => ui.Post(() =>
        {
            if (version != entry.ScheduleVersion || !isCurrent())
                return;

            entry.Due = true;
            RunIfDue(id, entry);
        }), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
    }

    private void RunIfDue(string id, Entry entry)
    {
        if (!entry.Due || entry.Busy || _stopped || _unsupported || !isCurrent())
            return;

        entry.Due = false;
        entry.Timer?.Dispose();
        entry.Timer = null;
        entry.Busy = true;
        var snapshot = entry.Draft;
        _ = RunAsync(id, entry, snapshot, entry.ResolveConflict);
    }

    private async Task RunAsync(string id, Entry entry, ChatDraft snapshot, bool conflict)
    {
        var outcome = Outcome.Retry;
        ChatDraftInfo? remoteDraft = null;
        var scope = (node, userId);
        try
        {
            if (!snapshot.Pending || conflict || !snapshot.HasContent && snapshot.Revision.Length == 0)
            {
                if (await remote.GetAsync(scope, id, lifetime).ConfigureAwait(false) is { } read)
                {
                    remoteDraft = read.Draft;
                    outcome = Outcome.Read;
                }
            }
            else if (snapshot.HasContent)
            {
                if (await remote.UpsertAsync(scope, id, snapshot.Text, snapshot.ReplyId, lifetime)
                        .ConfigureAwait(false) is { Draft.Revision.Length: > 0 } written)
                {
                    remoteDraft = written.Draft;
                    outcome = Outcome.Written;
                }
            }
            else if (await remote.DeleteAsync(scope, id, snapshot.Revision, lifetime).ConfigureAwait(false) is { } deleted)
                outcome = deleted.Deleted ? Outcome.Deleted : Outcome.Conflict;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Unimplemented)
        {
            outcome = Outcome.Unsupported;
        }
        catch (Exception exception) when (exception is MessageTextTooLongException or ChatNotFoundException
            or ChatNotRegularException or NoAccessToChatException or RpcException { StatusCode: StatusCode.FailedPrecondition })
        {
            outcome = Outcome.Rejected;
        }
        catch (MessageNotFoundException) when (snapshot.Pending && snapshot.HasContent && snapshot.ReplyId != 0)
        {
            outcome = Outcome.MissingReply;
        }
        catch (Exception)
        {
            // Локальная запись остаётся в журнале до следующей попытки.
        }

        ui.Post(() => Complete(id, entry, snapshot, outcome, remoteDraft));
    }

    private void Complete(string id, Entry entry, ChatDraft snapshot, Outcome outcome, ChatDraftInfo? remoteDraft)
    {
        if (_stopped || !isCurrent())
            return;

        entry.Busy = false;
        if (outcome == Outcome.Unsupported)
        {
            _unsupported = true;
            StopRemoteTimers();
            return;
        }

        if (!ReferenceEquals(entry.Draft, snapshot))
        {
            // Поздняя запись сообщает ревизию, которую должна учитывать текущая правка.
            if (outcome == Outcome.Written && remoteDraft is { } written)
                Update(id, entry, entry.Draft with { Revision = written.Revision }, applyToComposer: false);
            else if (outcome == Outcome.Deleted)
                Update(id, entry, entry.Draft with { Revision = "" }, applyToComposer: false);

            RunIfDue(id, entry);
            return;
        }

        switch (outcome)
        {
            case Outcome.Written:
                Update(id, entry, snapshot with { Pending = false, Revision = remoteDraft!.Revision }, applyToComposer: false);
                break;
            case Outcome.Deleted:
                Adopt(id, entry, null);
                break;
            case Outcome.Conflict:
                entry.ResolveConflict = true;
                Schedule(id, entry, entry.RetriedDelete ? NextRetry(entry) : 0);
                return;
            case Outcome.Read when remoteDraft is { } latest && snapshot.Pending &&
                (string.IsNullOrWhiteSpace(latest.Text) && latest.ReplyToMessageId == 0 ||
                 latest.Text.Trim() == snapshot.Text.Trim() && latest.ReplyToMessageId == snapshot.ReplyId):
                entry.ResolveConflict = false;
                entry.RetriedDelete = true;
                Update(id, entry, snapshot with { Revision = latest.Revision }, applyToComposer: false);
                Schedule(id, entry, latest.Revision.Length > 0 ? 0 : NextRetry(entry));
                return;
            case Outcome.Read:
                Adopt(id, entry, remoteDraft);
                break;
            case Outcome.Rejected:
                // Сервер отверг данные. Локальную правку можно исправить и отправить снова.
                Update(id, entry, snapshot with { Pending = false, LocalOnly = true }, applyToComposer: false);
                break;
            case Outcome.MissingReply:
                Set(id, snapshot.Type, snapshot.Text, 0);
                changed(id, true);
                return;
            default:
                if (entry.Open || snapshot.Pending)
                    Schedule(id, entry, NextRetry(entry));
                return;
        }

        entry.ResolveConflict = false;
        entry.RetriedDelete = false;
        entry.RetrySeconds = 2;
        entry.Due = false;
    }

    private void Adopt(string id, Entry entry, ChatDraftInfo? remoteDraft)
    {
        var draft = new ChatDraft(remoteDraft?.Text ?? "", remoteDraft?.ReplyToMessageId ?? 0,
            entry.Draft.Type, remoteDraft?.Revision ?? "");
        var previous = entry.Draft;
        var changedContent = draft.HasContent != previous.HasContent ||
            draft.HasContent && (draft.Text != previous.Text || draft.ReplyId != previous.ReplyId);
        if (changedContent)
            entry.Generation++;
        Update(id, entry, draft, applyToComposer: changedContent);
    }

    private static int NextRetry(Entry entry)
    {
        var delay = entry.RetrySeconds;
        entry.RetrySeconds = Math.Min(30, delay * 2);
        return delay;
    }
}
