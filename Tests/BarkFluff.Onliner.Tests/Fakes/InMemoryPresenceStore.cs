using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Tests.Fakes;

/// <summary>
/// In-memory реализация <see cref="IPresenceStore"/> для тестов, повторяющая семантику Redis-стора:
/// online = член множества; MarkOnline возвращает true при первом появлении; SetOffline (аналог ZREM)
/// возвращает true, только если пользователь был online; offline-записей не хранит.
/// </summary>
public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly Dictionary<long, DateTime> _online = new();

    public Task<bool> MarkOnlineAsync(long userId, CancellationToken ct = default)
    {
        var added = !_online.ContainsKey(userId);
        _online[userId] = DateTime.UtcNow;
        return Task.FromResult(added);
    }

    /// <summary>Тестовый помощник: задать last-seen напрямую (для проверки stale/offline-детекции).</summary>
    public void SetLastSeen(long userId, DateTime lastSeen) => _online[userId] = lastSeen;

    public Task<bool> SetOfflineAsync(long userId, CancellationToken ct = default)
        => Task.FromResult(_online.Remove(userId));

    public Task<UserOnlineStatus?> GetOnlineAsync(long userId, CancellationToken ct = default)
        => Task.FromResult(_online.TryGetValue(userId, out var lastSeen)
            ? new UserOnlineStatus { UserId = userId, Status = StatusTypeId.Online, LastSeen = lastSeen }
            : null);

    public Task<IReadOnlyList<UserOnlineStatus>> GetStaleUsersAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - threshold;
        IReadOnlyList<UserOnlineStatus> stale = _online
            .Where(kv => kv.Value <= cutoff)
            .Select(kv => new UserOnlineStatus { UserId = kv.Key, Status = StatusTypeId.Online, LastSeen = kv.Value })
            .ToList();
        return Task.FromResult(stale);
    }

    public Task<IReadOnlyCollection<UserOnlineStatus>> GetOnlineSnapshotAsync(CancellationToken ct = default)
    {
        IReadOnlyCollection<UserOnlineStatus> snapshot = _online
            .Select(kv => new UserOnlineStatus { UserId = kv.Key, Status = StatusTypeId.Online, LastSeen = kv.Value })
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task<long> GetOnlineCountAsync(CancellationToken ct = default)
        => Task.FromResult((long)_online.Count);
}
