using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;

public class UpdateUserBadgePriorityCommandHandler : IRequestHandler<UpdateUserBadgePriorityCommand, UpdateUserBadgePriorityResponse>
{
    private readonly UsersStorage _usersStorage;

    public UpdateUserBadgePriorityCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<UpdateUserBadgePriorityResponse> Handle(UpdateUserBadgePriorityCommand request, CancellationToken cancellationToken)
    {
        var userBadge = await _usersStorage.UpdateUserBadgePriorityAsync(
            request.UserId,
            request.BadgeId,
            request.NewPriority);

        if (userBadge == null)
        {
            throw new ArgumentException($"User badge not found for User {request.UserId} and Badge {request.BadgeId}");
        }

        return new UpdateUserBadgePriorityResponse
        {
            UserBadge = userBadge.ToGrpc()
        };
    }
}