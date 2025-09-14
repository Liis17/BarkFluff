using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.Queries;

public class GetAllBadgesQueryHandler : IRequestHandler<GetAllBadgesQuery, GetAllBadgesResponse>
{
    private readonly UsersStorage _usersStorage;

    public GetAllBadgesQueryHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<GetAllBadgesResponse> Handle(GetAllBadgesQuery request, CancellationToken cancellationToken)
    {
        var badges = await _usersStorage.GetAllBadgesAsync(request.IncludeInactive);

        var response = new GetAllBadgesResponse();
        response.Badges.AddRange(badges.Select(b => b.ToGrpc()));

        return response;
    }
}