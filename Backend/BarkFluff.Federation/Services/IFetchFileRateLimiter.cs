namespace BarkFluff.Federation.Services;

/// <summary>Лимит запросов файлов per-origin (этап 3.2). Сеам ради тестов без Redis.</summary>
public interface IFetchFileRateLimiter
{
    /// <summary>false — origin исчерпал минутный лимит.</summary>
    Task<bool> TryConsumeAsync(string origin);
}
