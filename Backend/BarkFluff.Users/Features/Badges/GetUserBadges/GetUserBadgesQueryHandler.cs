using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.GetUserBadges;

public class GetUserBadgesQueryHandler : IRequestHandler<GetUserBadgesQuery, GetUserBadgesResponse>
{
    private readonly UsersStorage _usersStorage;

    public GetUserBadgesQueryHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<GetUserBadgesResponse> Handle(GetUserBadgesQuery request, CancellationToken cancellationToken)
    {
        var userBadges = await _usersStorage.GetUserBadgesAsync(request.UserId, request.Limit);

        var response = new GetUserBadgesResponse();
        response.Badges.AddRange(userBadges.Select(ub => ub.ToGrpc()));

        return response;
    }
}