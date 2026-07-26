using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Tests.Fakes;

/// <summary>
/// In-memory реализация <see cref="IRemotePresenceStore"/> для тестов. Повторяет семантику
/// Redis-стора: отсутствие записи = «нода-партнёр статус не отдаёт» (её просто нет в результате),
/// а не Offline. TTL не моделируется — эвикцию в тестах изображает <see cref="RemoveAsync"/>.
/// </summary>
public sealed class InMemoryRemotePresenceStore : IRemotePresenceStore
{
    private readonly Dictionary<Guid, RemotePresenceEntry> _entries = new();

    /// <summary>Сколько ключей сейчас лежит в кеше — для проверок изоляции от локального presence.</summary>
    public int Count => _entries.Count;

    public Task UpsertAsync(Guid uuid, StatusTypeId status, DateTime lastSeen, CancellationToken ct = default)
    {
        _entries[uuid] = new RemotePresenceEntry(status, lastSeen);
        return Task.CompletedTask;
    }

    public Task<RemotePresenceEntry?> GetAsync(Guid uuid, CancellationToken ct = default)
        => Task.FromResult(_entries.TryGetValue(uuid, out var entry)
            ? (RemotePresenceEntry?)entry
            : null);

    public Task<IReadOnlyDictionary<Guid, RemotePresenceEntry>> GetManyAsync(
        IReadOnlyCollection<Guid> uuids,
        CancellationToken ct = default)
    {
        IReadOnlyDictionary<Guid, RemotePresenceEntry> found = uuids
            .Distinct()
            .Where(_entries.ContainsKey)
            .ToDictionary(uuid => uuid, uuid => _entries[uuid]);

        return Task.FromResult(found);
    }

    public Task RemoveAsync(Guid uuid, CancellationToken ct = default)
    {
        _entries.Remove(uuid);
        return Task.CompletedTask;
    }
}
