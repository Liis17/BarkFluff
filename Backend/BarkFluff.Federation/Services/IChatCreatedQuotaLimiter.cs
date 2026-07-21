namespace BarkFluff.Federation.Services;

public interface IChatCreatedQuotaLimiter
{
    /// <summary>Инкрементирует счётчик origin за текущий час; false — квота на этот час исчерпана.</summary>
    Task<bool> TryConsumeAsync(string origin);
}
