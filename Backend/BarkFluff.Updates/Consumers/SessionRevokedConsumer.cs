using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

namespace BarkFluff.Updates.Consumers;

public class SessionRevokedConsumer(
    TokenRevocationCache cache,
    ILogger<SessionRevokedConsumer> logger,
    MetricsCollector metrics)
    : IConsumer<SessionRevokedEvent>
{
    public Task Consume(ConsumeContext<SessionRevokedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("rabbitmq_events_consumed");
        metrics.Increment("session_revoked_events_consumed");
        metrics.Increment("sessions_revoked");
        logger.LogInformation(
            "Получено событие отзыва сессии: UserId={UserId}, DeviceId={DeviceId}",
            msg.UserId, msg.DeviceId);

        cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);
        return Task.CompletedTask;
    }
}
