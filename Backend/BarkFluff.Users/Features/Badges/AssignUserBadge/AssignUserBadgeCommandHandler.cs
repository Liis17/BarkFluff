using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.AssignUserBadge;

public class AssignUserBadgeCommandHandler : IRequestHandler<AssignUserBadgeCommand, AssignUserBadgeResponse>
{
    private readonly UsersStorage _usersStorage;

    public AssignUserBadgeCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<AssignUserBadgeResponse> Handle(AssignUserBadgeCommand request, CancellationToken cancellationToken)
    {
        var priority = request.Priority ?? 1000;
        var userBadge = await _usersStorage.AssignBadgeToUserAsync(request.UserId, request.BadgeId, priority);

        return new AssignUserBadgeResponse
        {
            UserBadge = userBadge.ToGrpc()
        };
    }
}