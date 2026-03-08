using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Badges.GetUserBadges;

public class GetUserBadgesQueryHandler : IRequestHandler<GetUserBadgesQuery, GetUserBadgesResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<GetUserBadgesQueryHandler> _logger;

    public GetUserBadgesQueryHandler(UsersStorage usersStorage, ILogger<GetUserBadgesQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<GetUserBadgesResponse> Handle(GetUserBadgesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Получение бейджей пользователя {UserId}. Лимит: {Limit}",
            request.UserId,
            request.Limit
        );

        var userBadges = await _usersStorage.GetUserBadgesAsync(request.UserId, request.Limit);

        _logger.LogInformation(
            "Получено {BadgeCount} бейджей для пользователя {UserId}",
            userBadges.Count(),
            request.UserId
        );

        var response = new GetUserBadgesResponse();
        response.Badges.AddRange(userBadges.Select(ub => ub.ToGrpc()));

        return response;
    }
}