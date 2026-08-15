namespace BarkFluff.Federation.Services;

/// <summary>
/// Распределённый single-runner: одну периодическую фоновую задачу выполняет один инстанс,
/// остальные пропускают тик (масштабирование, docs/scaling/federation.md). Best-effort: при
/// редком кратковременном двойном лидерстве задачи остаются корректными (чистка идемпотентна).
/// </summary>
public interface ISingleRunner
{
    /// <summary>Стать/остаться лидером для ключа на ttl. true — этот инстанс выполняет задачу в этот тик.</summary>
    Task<bool> TryAcquireAsync(string lockKey, TimeSpan ttl);
}
