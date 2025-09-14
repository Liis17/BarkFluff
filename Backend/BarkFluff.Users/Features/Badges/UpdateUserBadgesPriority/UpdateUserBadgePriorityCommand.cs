using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;

public class UpdateUserBadgePriorityCommand : IRequest<UpdateUserBadgePriorityResponse>
{
    public long UserId { get; init; }

    public int BadgeId { get; init; }

    public int NewPriority { get; init; }
}