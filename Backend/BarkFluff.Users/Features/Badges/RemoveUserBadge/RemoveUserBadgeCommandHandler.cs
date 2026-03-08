using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Badges.RemoveUserBadge;

public class RemoveUserBadgeCommandHandler : IRequestHandler<RemoveUserBadgeCommand, RemoveUserBadgeResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<RemoveUserBadgeCommandHandler> _logger;

    public RemoveUserBadgeCommandHandler(UsersStorage usersStorage, ILogger<RemoveUserBadgeCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<RemoveUserBadgeResponse> Handle(RemoveUserBadgeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Удаление бейджа {BadgeId} у пользователя {UserId}",
            request.BadgeId,
            request.UserId
        );

        var success = await _usersStorage.RemoveBadgeFromUserAsync(request.UserId, request.BadgeId);

        if (success)
        {
            _logger.LogInformation(
                "Бейдж {BadgeId} успешно удален у пользователя {UserId}",
                request.BadgeId,
                request.UserId
            );
        }
        else
        {
            _logger.LogWarning(
                "Не удалось удалить бейдж {BadgeId} у пользователя {UserId} (возможно, не найден)",
                request.BadgeId,
                request.UserId
            );
        }

        return new RemoveUserBadgeResponse
        {
            Success = success
        };
    }
}