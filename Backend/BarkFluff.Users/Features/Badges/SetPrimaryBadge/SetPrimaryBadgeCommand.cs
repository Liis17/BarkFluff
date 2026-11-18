using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Badges.SetPrimaryBadge;

public sealed class SetPrimaryBadgeCommand : IRequest<SetPrimaryBadgeResponse>
{
    public int BadgeId { get; init; }
}
