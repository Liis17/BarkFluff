namespace BarkFluff.Federation.Services;

/// <summary>
/// Rate-limit discovery-на-лету по неизвестному key_id (docs/rearch/03-discovery.md, "Политика
/// обновления"): без него флуд запросами со случайными key_id заставляет ноду долбить чужой
/// well-known. Cooldown общий на все инстансы (масштабирование, docs/scaling/federation.md).
/// </summary>
public interface IDiscoveryTriggerRateLimiter
{
    /// <summary>true — этот вызов первый в окне cooldown для serverName, discovery можно запускать.</summary>
    Task<bool> TryTriggerAsync(string serverName);
}
