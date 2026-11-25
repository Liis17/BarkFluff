using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.Core.Infrastructure.Realtime;

internal sealed class MessageUpdateJournal
{
    internal sealed class Change(long revision, MessageUpdateKind kind, bool sawNew,
        Google.Protobuf.WellKnownTypes.Timestamp? editTime, MessageUpdate? payload)
    {
        internal long Revision { get; } = revision;
        internal MessageUpdateKind Kind { get; } = kind;
        internal bool SawNew { get; } = sawNew;
        internal Google.Protobuf.WellKnownTypes.Timestamp? EditTime { get; } = editTime;
        internal MessageUpdate? Payload { get; set; } = payload;
        internal bool WaitingForNew { get; set; }
    }

    private readonly Dictionary<(string ChatId, bool IsPrivate), Dictionary<long, Change>> _messages = [];
    private readonly Dictionary<(string ChatId, bool IsPrivate), long> _chatRevisions = [];

    private readonly Dictionary<(string ChatId, bool IsPrivate), long> _unreadRevisions = [];
    private readonly Dictionary<(string ChatId, bool IsPrivate), int> _readers = [];
    private readonly Dictionary<(string ChatId, bool IsPrivate), HashSet<Change>> _payloads = [];

    internal long UnreadRevision(string chatId, bool isPrivate) => _unreadRevisions.GetValueOrDefault((chatId, isPrivate));

    internal void TouchUnread(string chatId, bool isPrivate) => _unreadRevisions[(chatId, isPrivate)] = ++Revision;

    internal IDisposable ReadHistory(string chatId, bool isPrivate)
    {
        var key = (chatId, isPrivate);
        _readers[key] = _readers.GetValueOrDefault(key) + 1;
        return new HistoryReader(() =>
        {
            if (--_readers[key] == 0)
            {
                _readers.Remove(key);
                ReleasePayloads(key, includeWaiting: false);
            }
        });
    }

    internal void LeaveChat(string chatId, bool isPrivate) => ReleasePayloads((chatId, isPrivate), includeWaiting: true);

    private void ReleasePayloads((string, bool) key, bool includeWaiting)
    {
        if (!_payloads.TryGetValue(key, out var changes))
        {
            return;
        }

        foreach (var change in changes)
        {
            if (includeWaiting)
            {
                change.WaitingForNew = false;
            }
        }

        if (_readers.ContainsKey(key))
        {
            return;
        }

        changes.RemoveWhere(change =>
        {
            if (change.WaitingForNew)
            {
                return false;
            }

            change.Payload = null;
            return true;
        });

        if (changes.Count == 0)
        {
            _payloads.Remove(key);
        }
    }

    private sealed class HistoryReader(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    internal MessageUpdate[] NewMessagesSince(string chatId, bool isPrivate, long revision) =>
        _payloads.TryGetValue((chatId, isPrivate), out var changes)
            ? changes.Where(change => change.SawNew && change.Revision > revision && change.Payload is not null)
                .Select(change => change.Payload!).ToArray()
            : [];

    internal long Revision { get; private set; }

    internal long ChatRevision(string chatId, bool isPrivate) => _chatRevisions.GetValueOrDefault((chatId, isPrivate));

    private long Touch(string chatId, bool isPrivate) => _chatRevisions[(chatId, isPrivate)] = ++Revision;

    internal Change? Find(string chatId, bool isPrivate, long id) =>
        _messages.GetValueOrDefault((chatId, isPrivate))?.GetValueOrDefault(id);

    internal Change? Record(MessageUpdate update)
    {
        var key = (update.ChatId, update.IsPrivate);
        if (!_messages.TryGetValue(key, out var entries))
        {
            _messages[key] = entries = [];
        }

        var previous = entries.GetValueOrDefault(update.MessageId);
        if (previous?.Kind == MessageUpdateKind.Delete)
        {
            return null;
        }

        if (update.Kind == MessageUpdateKind.New && previous is { SawNew: true })
        {
            return null;
        }

        if (update.Kind == MessageUpdateKind.Edit && previous?.Kind == MessageUpdateKind.Edit &&
            Comparer<Google.Protobuf.WellKnownTypes.Timestamp?>.Default.Compare(EditedAt(update), previous.EditTime) <= 0)
        {
            return null;
        }

        var sawNew = update.Kind == MessageUpdateKind.New || previous?.SawNew == true;
        // При запоздалом New версия правки остаётся приоритетной даже после освобождения payload.
        var preserveEdit = update.Kind == MessageUpdateKind.New && previous?.Kind == MessageUpdateKind.Edit;
        var change = new Change(Touch(update.ChatId, update.IsPrivate),
            preserveEdit ? MessageUpdateKind.Edit : update.Kind,
            sawNew, preserveEdit ? previous!.EditTime : EditedAt(update),
            preserveEdit ? previous!.Payload : update.Kind == MessageUpdateKind.Delete ? null : update);

        if (update.Kind is MessageUpdateKind.New or MessageUpdateKind.Delete)
        {
            TouchUnread(update.ChatId, update.IsPrivate);
        }

        if (!_payloads.TryGetValue(key, out var payloads) && change.Payload is not null)
        {
            _payloads[key] = payloads = [];
        }
        if (previous is not null)
        {
            payloads?.Remove(previous);
        }
        if (change.Payload is not null)
        {
            payloads!.Add(change);
        }
        else if (payloads?.Count == 0)
        {
            _payloads.Remove(key);
        }
        entries[update.MessageId] = change;
        return change;
    }

    internal void Release(string chatId, bool isPrivate, Change change)
    {
        var key = (chatId, isPrivate);
        if (change.WaitingForNew || _readers.ContainsKey(key))
        {
            return;
        }

        change.Payload = null;
        if (_payloads.TryGetValue(key, out var changes) && changes.Remove(change) && changes.Count == 0)
        {
            _payloads.Remove(key);
        }
    }

    internal static Google.Protobuf.WellKnownTypes.Timestamp? EditedAt(MessageUpdate update) =>
        update.Message?.EditedAt ?? update.Encrypted?.EditedAt;
}
