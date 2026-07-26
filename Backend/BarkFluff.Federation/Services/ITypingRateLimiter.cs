namespace BarkFluff.Federation.Services;

/// <summary>Лимит входящих typing per-origin (этап 4.4). Сеам ради тестов без Redis.</summary>
public interface ITypingRateLimiter
{
    /// <summary>false — origin исчерпал минутный лимит.</summary>
    Task<bool> TryConsumeAsync(string origin);
}
