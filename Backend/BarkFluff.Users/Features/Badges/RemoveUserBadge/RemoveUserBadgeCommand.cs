using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Badges.RemoveUserBadge;

public class RemoveUserBadgeCommand : IRequest<RemoveUserBadgeResponse>
{
    public long UserId { get; init; }

    public int BadgeId { get; init; }
}