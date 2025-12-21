using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;

public class UpdateUserBadgePriorityCommandHandler : IRequestHandler<UpdateUserBadgePriorityCommand, UpdateUserBadgePriorityResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<UpdateUserBadgePriorityCommandHandler> _logger;

    public UpdateUserBadgePriorityCommandHandler(UsersStorage usersStorage, ILogger<UpdateUserBadgePriorityCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<UpdateUserBadgePriorityResponse> Handle(UpdateUserBadgePriorityCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Обновление приоритета бейджа {BadgeId} для пользователя {UserId}. Новый приоритет: {NewPriority}",
            request.BadgeId,
            request.UserId,
            request.NewPriority
        );

        var userBadge = await _usersStorage.UpdateUserBadgePriorityAsync(
            request.UserId,
            request.BadgeId,
            request.NewPriority);

        if (userBadge == null)
        {
            _logger.LogWarning(
                "Бейдж {BadgeId} не найден у пользователя {UserId}",
                request.BadgeId,
                request.UserId
            );
            throw new ArgumentException($"User badge not found for User {request.UserId} and Badge {request.BadgeId}");
        }

        _logger.LogInformation(
            "Приоритет бейджа {BadgeId} успешно обновлен для пользователя {UserId}",
            request.BadgeId,
            request.UserId
        );

        return new UpdateUserBadgePriorityResponse
        {
            UserBadge = userBadge.ToGrpc()
        };
    }
}