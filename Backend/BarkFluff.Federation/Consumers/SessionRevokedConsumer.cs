using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using Microsoft.Extensions.Logging;

namespace BarkFluff.Federation.Consumers;

// Стандартная инвалидация XAuth-токенов (по образцу Users/Messages/Updates/Onliner/Files/Identity/Calls).
// Каркас 1.1 не завёл этот консюмер в Federation — добавляем в 2.2.
public class SessionRevokedConsumer : IConsumer<SessionRevokedEvent>
{
    private readonly TokenRevocationCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SessionRevokedConsumer> _logger;

    public SessionRevokedConsumer(
        TokenRevocationCache cache,
        MetricsCollector metrics,
        ILogger<SessionRevokedConsumer> logger)
    {
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<SessionRevokedEvent> context)
    {
        var msg = context.Message;
        _metrics.Increment("session_revoked_received");
        _logger.LogInformation("Получено событие отзыва сессии: UserId={UserId}, DeviceId={DeviceId}",
            msg.UserId, msg.DeviceId);
        _cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);
        _metrics.Set("last_session_revoked_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Task.CompletedTask;
    }
}
