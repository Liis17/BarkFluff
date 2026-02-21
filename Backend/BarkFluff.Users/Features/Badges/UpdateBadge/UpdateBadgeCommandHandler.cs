using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.UpdateBadge;

public class UpdateBadgeCommandHandler : IRequestHandler<UpdateBadgeCommand, UpdateBadgeResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<UpdateBadgeCommandHandler> _logger;

    public UpdateBadgeCommandHandler(UsersStorage usersStorage, ILogger<UpdateBadgeCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<UpdateBadgeResponse> Handle(UpdateBadgeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обновление бейджа {BadgeId}", request.Id);

        var badge = await _usersStorage.UpdateBadgeAsync(
            request.Id,
            request.Name,
            request.Description,
            request.ImageUrl,
            request.IsActive
        );

        if (badge is null)
        {
            _logger.LogWarning("Бейдж {BadgeId} не найден", request.Id);
            throw new ArgumentException($"Badge with id {request.Id} not found");
        }

        _logger.LogInformation("Бейдж {BadgeId} успешно обновлён", request.Id);

        return new UpdateBadgeResponse
        {
            Badge = badge.ToGrpc()
        };
    }
}
