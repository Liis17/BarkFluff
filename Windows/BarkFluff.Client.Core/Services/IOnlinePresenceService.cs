namespace BarkFluff.Client.Core.Services;

public sealed record UserPresence(long UserId, bool IsOnline, DateTimeOffset LastSeen);

public interface IOnlinePresenceService : IAsyncDisposable
{
    event EventHandler<UserPresence>? PresenceChanged;

    /// <summary>
    /// Последний известный статус пользователя; <c>null</c>, если по нему ещё ничего не приходило.
    /// Нужен экранам, которые открываются без собственной подписки — например, профилю собеседника.
    /// </summary>
    UserPresence? TryGet(long userId);

    /// <summary>
    /// Запускает стрим статусов и keepalive. Повторный вызов с другим набором пользователей
    /// меняет подписку на месте, не пересоздавая стрим.
    /// </summary>
    Task WatchAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    Task StopAsync();
}
