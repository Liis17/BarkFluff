using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.RemoveUserBadge;

public class RemoveUserBadgeCommandHandler : IRequestHandler<RemoveUserBadgeCommand, RemoveUserBadgeResponse>
{
    private readonly UsersStorage _usersStorage;

    public RemoveUserBadgeCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<RemoveUserBadgeResponse> Handle(RemoveUserBadgeCommand request, CancellationToken cancellationToken)
    {
        var success = await _usersStorage.RemoveBadgeFromUserAsync(request.UserId, request.BadgeId);

        return new RemoveUserBadgeResponse
        {
            Success = success
        };
    }
}