namespace BarkFluff.Federation.Services;

public interface IChatCreatedQuotaLimiter
{
    /// <summary>
    /// Инкрементирует счётчик origin за текущий час; false — квота на этот час исчерпана.
    /// Списание идемпотентно по eventId — повторная доставка/ретрай того же события не тратит
    /// квоту повторно.
    /// </summary>
    Task<bool> TryConsumeAsync(string origin, Guid eventId);
}
