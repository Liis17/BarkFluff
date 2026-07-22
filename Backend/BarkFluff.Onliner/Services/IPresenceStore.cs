using BarkFluff.Onliner.Domain.Entities;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Общий источник истины о присутствии пользователей (online/last-seen), разделяемый всеми
/// инстансами Onliner. Прод-реализация — Redis (<see cref="RedisPresenceStore"/>); тесты
/// используют in-memory fake. Заменяет прежнее per-instance in-memory хранилище.
/// </summary>
public interface IPresenceStore
{
    /// <summary>Отметить heartbeat пользователя. true — переход в Online (был offline/absent).</summary>
    Task<bool> MarkOnlineAsync(long userId, CancellationToken ct = default);

    /// <summary>Снять онлайн. true — пользователь был online (произошёл переход в Offline).</summary>
    Task<bool> SetOfflineAsync(long userId, CancellationToken ct = default);

    /// <summary>Текущий статус, если пользователь online; иначе null (offline last-seen берётся из БД).</summary>
    Task<UserOnlineStatus?> GetOnlineAsync(long userId, CancellationToken ct = default);

    /// <summary>Онлайн-пользователи, чей последний heartbeat старше threshold (кандидаты в offline).</summary>
    Task<IReadOnlyList<UserOnlineStatus>> GetStaleUsersAsync(TimeSpan threshold, CancellationToken ct = default);

    /// <summary>Снимок всех онлайн-пользователей (для персиста last-seen в БД).</summary>
    Task<IReadOnlyCollection<UserOnlineStatus>> GetOnlineSnapshotAsync(CancellationToken ct = default);

    /// <summary>Количество онлайн-пользователей (для метрик).</summary>
    Task<long> GetOnlineCountAsync(CancellationToken ct = default);
}
