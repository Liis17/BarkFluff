using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Badges.AssignUserBadge;

public class AssignUserBadgeCommand : IRequest<AssignUserBadgeResponse>
{
    public long UserId { get; init; }

    public int BadgeId { get; init; }

    public int? Priority { get; init; }
}