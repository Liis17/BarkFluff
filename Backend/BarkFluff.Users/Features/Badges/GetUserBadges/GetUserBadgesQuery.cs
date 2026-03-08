using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Badges.GetUserBadges;

public class GetUserBadgesQuery : IRequest<GetUserBadgesResponse>
{
    public long UserId { get; init; }

    public int? Limit { get; init; }
}