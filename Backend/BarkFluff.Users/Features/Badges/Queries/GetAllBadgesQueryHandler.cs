using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Badges.Queries;

public class GetAllBadgesQueryHandler : IRequestHandler<GetAllBadgesQuery, GetAllBadgesResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<GetAllBadgesQueryHandler> _logger;

    public GetAllBadgesQueryHandler(UsersStorage usersStorage, ILogger<GetAllBadgesQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<GetAllBadgesResponse> Handle(GetAllBadgesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Получение всех бейджей. IncludeInactive: {IncludeInactive}",
            request.IncludeInactive
        );

        var badges = await _usersStorage.GetAllBadgesAsync(request.IncludeInactive);

        _logger.LogInformation(
            "Получено {BadgeCount} бейджей (IncludeInactive: {IncludeInactive})",
            badges.Count(),
            request.IncludeInactive
        );

        var response = new GetAllBadgesResponse();
        response.Badges.AddRange(badges.Select(b => b.ToGrpc()));

        return response;
    }
}