using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.Core.Services;

// Приватное сообщение представлено только серверным конвертом.
public sealed record CachedHistoryMessage(long Id, DateTimeOffset SentAt, MessageModel? Regular, EncryptedMessage? Encrypted)
{
    internal long EditTime => (Regular?.EditedAt ?? Encrypted?.EditedAt)?.ToDateTimeOffset().UtcTicks ?? 0;
}

public sealed record CachedHistoryPage(IReadOnlyList<CachedHistoryMessage> Messages, bool IncludesTail,
    IReadOnlyList<PinnedMessageInfo>? Pins = null, bool HasOlder = false);

public sealed record HistoryCacheWrite(string ChatId, IReadOnlyList<CachedHistoryMessage> Messages,
    ((DateTimeOffset Time, long Id) Start, (DateTimeOffset Time, long Id) End)? Range = null,
    long? DeleteId = null, bool Append = false, IReadOnlyList<PinnedMessageInfo>? Pins = null,
    MessageUpdate? PinChange = null);

public interface IMessageHistoryCacheStore
{
    CachedHistoryPage Read(ChatListScope scope, string chatId, bool isPrivate, long anchor, int pageSize = 50, bool older = false);
    void Write(ChatListScope scope, IReadOnlyList<HistoryCacheWrite> writes);
    bool Clear(ChatListScope scope);
    long Size(ChatListScope scope);
}

public sealed class MessageHistoryCache(IMessageHistoryCacheStore store)
{
    private readonly object _gate = new();
    private readonly Dictionary<ChatListScope, long> _generations = [];
    private readonly Dictionary<ChatListScope, List<HistoryCacheWrite>> _pending = [];
    private Task _tail = Task.CompletedTask;

    public long Generation(ChatListScope scope)
    {
        lock (_gate)
            return _generations.GetValueOrDefault(scope);
    }

    public void Schedule(ChatListScope scope, long generation, HistoryCacheWrite write)
    {
        lock (_gate)
        {
            if (generation != _generations.GetValueOrDefault(scope))
                return;
            if (_pending.TryGetValue(scope, out var batch))
            {
                batch.Add(write);
                return;
            }
            batch = [write];
            _pending.Add(scope, batch);
            Enqueue(() =>
            {
                HistoryCacheWrite[] writes;
                lock (_gate)
                {
                    if (_pending.GetValueOrDefault(scope) == batch)
                        _pending.Remove(scope);
                    writes = batch.ToArray();
                }
                if (writes.Length > 0)
                    store.Write(scope, writes);
                return true;
            });
        }
    }

    public Task<CachedHistoryPage> ReadAsync(ChatListScope scope, string chatId, bool isPrivate, long anchor,
        int pageSize, bool older)
    {
        lock (_gate)
            return Enqueue(() => store.Read(scope, chatId, isPrivate, anchor, pageSize, older));
    }

    public Task<long> SizeAsync(ChatListScope scope)
    {
        lock (_gate)
            return Enqueue(() => store.Size(scope));
    }

    public Task<bool> ClearAsync(ChatListScope scope)
    {
        lock (_gate)
        {
            _generations[scope] = _generations.GetValueOrDefault(scope) + 1;
            if (_pending.Remove(scope, out var batch))
                batch.Clear();
            return Enqueue(() => store.Clear(scope));
        }
    }

    public Task FlushAsync()
    {
        lock (_gate)
            return _tail;
    }

    private Task<T> Enqueue<T>(Func<T> operation)
    {
        var previous = _tail;
        var next = Task.Run(async () =>
        {
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            return operation();
        });
        _tail = next;
        return next;
    }
}

