using System.Threading.Channels;

using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Domain;

/// <summary>
/// Хранилище QR-сессий, общее для всех инстансов (Redis). Все переходы состояния
/// атомарны на стороне стора, поэтому гонки параллельных Scan/Accept/Reject
/// решаются так же, как раньше in-process lock.
/// </summary>
public interface IFastAuthSessionStore
{
    Task<FastAuthSessionState> CreateAsync(string deviceName, string operationSystem,
        string appName, string appVersion, string ipAddress, CancellationToken ct = default);

    Task<FastAuthSessionState?> GetAsync(string id, CancellationToken ct = default);

    Task<FastAuthTransition> TryScanAsync(string id, long userId, string confirmationCode,
        CancellationToken ct = default);

    Task<FastAuthTransition> TryAcceptAsync(string id, string confirmationCode, long userId,
        FastAuthSessionResult result, CancellationToken ct = default);

    Task<FastAuthTransition> TryRejectAsync(string id, string confirmationCode, long userId,
        CancellationToken ct = default);

    Task<bool> TryExpireAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Захватывает единственного подписчика стрима за сессией (глобально, на все инстансы).
    /// Возвращает токен владельца или null, если подписчик уже закреплён.
    /// </summary>
    Task<string?> TryAttachSubscriberAsync(string id, TimeSpan lockTtl, CancellationToken ct = default);

    /// <summary>Освобождает захват подписчика, если он принадлежит этому владельцу.</summary>
    Task ReleaseSubscriberAsync(string id, string ownerToken, CancellationToken ct = default);
}

/// <summary>
/// Доставка событий сессии ожидающему стриму: переход (Scan/Accept/Reject) может случиться
/// на одном инстансе, а клиент с открытым стримом ждать на другом.
/// </summary>
public interface IFastAuthEventBus
{
    Task PublishAsync(string sessionId, FastAuthResult result, CancellationToken ct = default);

    /// <summary>Регистрирует локального ожидающего. null — на этом инстансе уже есть ожидающий.</summary>
    ChannelReader<FastAuthResult>? Attach(string sessionId);

    void Detach(string sessionId);
}
