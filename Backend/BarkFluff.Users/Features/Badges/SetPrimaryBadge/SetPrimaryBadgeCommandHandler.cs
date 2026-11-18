using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Users.Features.Badges.SetPrimaryBadge;

public sealed class SetPrimaryBadgeCommandHandler(
    UsersStorage usersStorage,
    UserContext userContext,
    ILogger<SetPrimaryBadgeCommandHandler> logger)
    : IRequestHandler<SetPrimaryBadgeCommand, SetPrimaryBadgeResponse>
{
    public async Task<SetPrimaryBadgeResponse> Handle(SetPrimaryBadgeCommand request, CancellationToken cancellationToken)
    {
        if (request.BadgeId < 0)
        {
            throw new BadgeException(StatusCode.InvalidArgument, "badge_id не может быть отрицательным");
        }

        logger.LogInformation(
            "Установка основного бейджа {BadgeId} для пользователя {UserId}",
            request.BadgeId,
            userContext.UserId);

        var primaryBadge = await usersStorage.SetPrimaryBadgeAsync(
            userContext.UserId,
            request.BadgeId,
            cancellationToken);

        var response = new SetPrimaryBadgeResponse();
        if (primaryBadge is not null)
        {
            response.PrimaryBadge = primaryBadge.ToGrpc();
        }

        return response;
    }
}
