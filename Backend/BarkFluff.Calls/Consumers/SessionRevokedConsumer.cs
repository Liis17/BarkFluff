using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

namespace BarkFluff.Calls.Consumers;

/// <summary>
/// Инвалидация отозванной сессии в локальном кэше токенов (как в остальных сервисах).
/// </summary>
public class SessionRevokedConsumer(
    TokenRevocationCache cache,
    MetricsCollector metrics,
    ILogger<SessionRevokedConsumer> logger)
    : IConsumer<SessionRevokedEvent>
{
    public Task Consume(ConsumeContext<SessionRevokedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("sessions_revoked");
        logger.LogInformation(
            "Получено событие отзыва сессии: UserId={UserId}, DeviceId={DeviceId}",
            msg.UserId, msg.DeviceId);

        cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);
        return Task.CompletedTask;
    }
}
