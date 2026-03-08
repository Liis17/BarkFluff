using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Badges.Queries;

public class GetAllBadgesQuery : IRequest<GetAllBadgesResponse>
{
    public bool IncludeInactive { get; init; }
}