using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

public readonly record struct ChatListScope(string Node, long UserId)
{
    public static ChatListScope Create(string node, long userId) => new(Normalize(node), userId);
    public static bool SameNode(string left, string right) => Normalize(left) == Normalize(right);
    private static string Normalize(string node) => node.Trim().TrimEnd('/').ToUpperInvariant();
}

public sealed record CachedChat(string Id, byte[] Definition, string Title, string FirstName, string LastName,
    string Username, long? PeerId, AccountStatus AccountStatus, string Preview, long LastMessageId,
    DateTimeOffset? LastMessageAt, DateTimeOffset? EditedAt)
{
    public string? AvatarFileId { get; init; }
}

public sealed record CachedChatList(IReadOnlyList<CachedChat> Chats, IReadOnlyList<ChatFolderData> Folders);

public interface IChatListCacheStore
{
    CachedChatList Load(ChatListScope scope);
    void Save(ChatListScope scope, CachedChatList list);
    bool Clear(ChatListScope scope);
    long Size(ChatListScope scope);
}

public sealed class ChatListCache(IChatListCacheStore store, MessageHistoryCache? history = null)
{
    internal MessageHistoryCache? History => history;
    private sealed class Work
    {
        public CachedChatList? Pending { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<ChatListScope, Work> _writes = [];
    private Task _tail = Task.CompletedTask;

    public Task<CachedChatList> LoadAsync(ChatListScope scope) => Task.Run(() => store.Load(scope));
    public async Task<long> SizeAsync(ChatListScope scope)
    {
        var listSize = Task.Run(() => store.Size(scope));
        return await listSize.ConfigureAwait(false) +
            (history is null ? 0 : await history.SizeAsync(scope).ConfigureAwait(false));
    }

    public void ScheduleSave(ChatListScope scope, CachedChatList list)
    {
        lock (_gate)
        {
            if (_writes.TryGetValue(scope, out var work))
            {
                work.Pending = list;
                return;
            }

            work = new Work { Pending = list };
            _writes.Add(scope, work);
            var previous = _tail;
            _tail = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                Drain(scope, work);
            });
        }
    }

    public Task FlushAsync()
    {
        lock (_gate)
            return history is null ? _tail : Task.WhenAll(_tail, history.FlushAsync());
    }

    public async Task<bool> ClearAsync(ChatListScope scope)
    {
        var clearingHistory = history?.ClearAsync(scope);
        var clearedList = await ClearListAsync(scope).ConfigureAwait(false);
        return (clearingHistory is null || await clearingHistory.ConfigureAwait(false)) && clearedList;
    }

    private Task<bool> ClearListAsync(ChatListScope scope)
    {
        lock (_gate)
        {
            if (_writes.Remove(scope, out var work))
                work.Pending = null;
            var previous = _tail;
            var clearing = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                return store.Clear(scope);
            });
            _tail = clearing;
            return clearing;
        }
    }

    private void Drain(ChatListScope scope, Work work)
    {
        while (true)
        {
            CachedChatList list;
            lock (_gate)
            {
                if (work.Pending is not { } pending)
                {
                    if (_writes.GetValueOrDefault(scope) == work)
                        _writes.Remove(scope);
                    return;
                }
                list = pending;
                work.Pending = null;
            }

            store.Save(scope, list);
        }
    }
}
