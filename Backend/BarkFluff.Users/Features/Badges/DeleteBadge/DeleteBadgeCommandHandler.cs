using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.Badges.DeleteBadge;

public class DeleteBadgeCommandHandler : IRequestHandler<DeleteBadgeCommand, DeleteBadgeResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<DeleteBadgeCommandHandler> _logger;

    public DeleteBadgeCommandHandler(UsersStorage usersStorage, ILogger<DeleteBadgeCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<DeleteBadgeResponse> Handle(DeleteBadgeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Удаление бейджа {BadgeId}", request.Id);

        var success = await _usersStorage.DeleteBadgeAsync(request.Id);

        if (!success)
        {
            _logger.LogWarning("Бейдж {BadgeId} не найден при попытке удаления", request.Id);
        }
        else
        {
            _logger.LogInformation("Бейдж {BadgeId} успешно удалён", request.Id);
        }

        return new DeleteBadgeResponse
        {
            Success = success
        };
    }
}
