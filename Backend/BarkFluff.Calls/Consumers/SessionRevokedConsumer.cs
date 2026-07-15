using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using Livekit.Server.Sdk.Dotnet;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Calls.Consumers;

/// <summary>
/// Инвалидация отозванной сессии в локальном кэше токенов (как в остальных сервисах)
/// + best-effort кик участника из активных LiveKit-комнат, чтобы отозванная сессия
/// не могла продолжать публиковать/получать медиа до истечения LiveKit JWT (TTL 2 часа).
/// </summary>
public class SessionRevokedConsumer(
    TokenRevocationCache cache,
    CallsContext db,
    RoomServiceClient roomService,
    MetricsCollector metrics,
    ILogger<SessionRevokedConsumer> logger)
    : IConsumer<SessionRevokedEvent>
{
    public async Task Consume(ConsumeContext<SessionRevokedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("sessions_revoked");
        logger.LogInformation(
            "Получено событие отзыва сессии: UserId={UserId}, DeviceId={DeviceId}",
            msg.UserId, msg.DeviceId);

        cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);

        await KickFromActiveCallsAsync(msg.UserId, context.CancellationToken);
    }

    /// <summary>
    /// LiveKit identity — всегда userId (без device-scope, см. CallsService.CreateToken),
    /// поэтому отзыв одной сессии кикает пользователя из звонка независимо от устройства.
    /// Групповые комнаты не хранят список участников в БД, поэтому пробуем удалить из всех
    /// активных групповых комнат — RemoveParticipant на отсутствующем участнике не считается ошибкой.
    /// </summary>
    private async Task KickFromActiveCallsAsync(long userId, CancellationToken ct)
    {
        var identity = userId.ToString();

        var roomNames = await db.CallSessions.AsNoTracking()
            .Where(c => c.Status == CallStatus.Active &&
                (c.CallerUserId == userId || c.CalleeUserId == userId || c.ChatId != null))
            .Select(c => c.RoomName)
            .Distinct()
            .ToListAsync(ct);

        foreach (var room in roomNames)
        {
            try
            {
                await roomService.RemoveParticipant(new RoomParticipantIdentity { Room = room, Identity = identity });
            }
            catch (Exception ex)
            {
                // Участника может не быть в комнате (не звонил / уже вышел) — это ожидаемо, не ошибка.
                logger.LogDebug(ex, "Не удалось удалить {Identity} из LiveKit-комнаты {Room} (возможно, там не было)", identity, room);
            }
        }
    }
}
