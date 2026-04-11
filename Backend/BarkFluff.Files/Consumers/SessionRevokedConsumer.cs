using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using Microsoft.Extensions.Logging;

namespace BarkFluff.Files.Consumers;

public class SessionRevokedConsumer(TokenRevocationCache cache, ILogger<SessionRevokedConsumer> logger)
    : IConsumer<SessionRevokedEvent>
{
    public Task Consume(ConsumeContext<SessionRevokedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation(
            "Получено событие отзыва сессии: UserId={UserId}, DeviceId={DeviceId}",
            msg.UserId, msg.DeviceId);

        cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);
        return Task.CompletedTask;
    }
}
