using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;

using Livekit.Server.Sdk.Dotnet;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Calls.Consumers;

/// <summary>
/// Best-effort кик из LiveKit-комнаты при исключении пользователя из чата (KickUser в Messages).
/// В отличие от SessionRevokedConsumer здесь известен ChatId — не нужно перебирать все активные комнаты.
/// </summary>
public class ChatMemberKickedConsumer(
    CallsContext db,
    RoomServiceClient roomService,
    MetricsCollector metrics,
    ILogger<ChatMemberKickedConsumer> logger)
    : IConsumer<ChatMemberKickedEvent>
{
    public async Task Consume(ConsumeContext<ChatMemberKickedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("chat_member_kicked");

        var roomName = await db.CallSessions.AsNoTracking()
            .Where(c => c.Status == CallStatus.Active && c.ChatId == msg.ChatId)
            .Select(c => c.RoomName)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (roomName is null)
        {
            return;
        }

        var identity = msg.UserId.ToString();

        try
        {
            await roomService.RemoveParticipant(new RoomParticipantIdentity { Room = roomName, Identity = identity });
            logger.LogInformation("Пользователь {UserId} удалён из LiveKit-комнаты {Room} (исключён из чата {ChatId})",
                msg.UserId, roomName, msg.ChatId);
        }
        catch (Exception ex)
        {
            // Пользователя может не быть в звонке — это ожидаемо, не ошибка.
            logger.LogDebug(ex, "Не удалось удалить {Identity} из LiveKit-комнаты {Room} (возможно, там не было)", identity, roomName);
        }
    }
}
