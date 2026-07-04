using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.SetChatMuted;

public class SetChatMutedCommandHandler(
    ChatMuteStorage chatMuteStorage,
    UserContext userContext,
    ILogger<SetChatMutedCommandHandler> logger)
    : IRequestHandler<SetChatMutedCommand, Unit>
{
    public async Task<Unit> Handle(SetChatMutedCommand request, CancellationToken cancellationToken)
    {
        if (request.Muted)
        {
            await chatMuteStorage.SetMuted(userContext.UserId, request.ChatId, request.MutedUntil);
            logger.LogInformation(
                "Чат {ChatId} замьючен пользователем {UserId} (до {MutedUntil})",
                request.ChatId, userContext.UserId, request.MutedUntil?.ToString("o") ?? "навсегда");
        }
        else
        {
            await chatMuteStorage.Unmute(userContext.UserId, request.ChatId);
            logger.LogInformation(
                "Mute снят с чата {ChatId} пользователем {UserId}",
                request.ChatId, userContext.UserId);
        }

        return Unit.Value;
    }
}
