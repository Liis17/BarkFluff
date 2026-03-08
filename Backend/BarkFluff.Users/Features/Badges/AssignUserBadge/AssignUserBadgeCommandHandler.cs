using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Badges.AssignUserBadge;

public class AssignUserBadgeCommandHandler : IRequestHandler<AssignUserBadgeCommand, AssignUserBadgeResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<AssignUserBadgeCommandHandler> _logger;

    public AssignUserBadgeCommandHandler(UsersStorage usersStorage, ILogger<AssignUserBadgeCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<AssignUserBadgeResponse> Handle(AssignUserBadgeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Назначение бейджа {BadgeId} пользователю {UserId} с приоритетом {Priority}",
            request.BadgeId,
            request.UserId,
            request.Priority ?? 1000
        );

        var priority = request.Priority ?? 1000;
        var userBadge = await _usersStorage.AssignBadgeToUserAsync(request.UserId, request.BadgeId, priority);

        _logger.LogInformation(
            "Бейдж {BadgeId} успешно назначен пользователю {UserId}",
            request.BadgeId,
            request.UserId
        );

        return new AssignUserBadgeResponse
        {
            UserBadge = userBadge.ToGrpc()
        };
    }
}