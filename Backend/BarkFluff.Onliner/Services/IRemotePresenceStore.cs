using BarkFluff.Onliner.Domain.Enums;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Кеш статусов remote-пользователей (этап 4.2). Источник истины — чужая нода, поэтому здесь
/// именно кеш: он не персистится, не участвует в offline-детекции и эвикцируется по TTL.
/// Прод-реализация — <see cref="RedisRemotePresenceStore"/>; тесты используют in-memory fake.
/// </summary>
public interface IRemotePresenceStore
{
    /// <summary>Записать статус remote-пользователя и продлить TTL ключа.</summary>
    Task UpsertAsync(Guid uuid, StatusTypeId status, DateTime lastSeen, CancellationToken ct = default);

    /// <summary>Статус одного uuid; null — ключа нет (нода-партнёр статус не отдаёт).</summary>
    Task<RemotePresenceEntry?> GetAsync(Guid uuid, CancellationToken ct = default);

    /// <summary>Статусы батчем. Отсутствующие uuid в результат не попадают.</summary>
    Task<IReadOnlyDictionary<Guid, RemotePresenceEntry>> GetManyAsync(
        IReadOnlyCollection<Guid> uuids, CancellationToken ct = default);

    /// <summary>Явное гашение статуса (обрыв S2S-стрима шлёт UNKNOWN, этот метод — путь очистки).</summary>
    Task RemoveAsync(Guid uuid, CancellationToken ct = default);
}

/// <summary>Запись кеша remote-статусов: статус и момент, на который он актуален.</summary>
public readonly record struct RemotePresenceEntry(StatusTypeId Status, DateTime LastSeen);
