using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ConfirmUser;

public class ConfirmUserCommandHandler : IRequestHandler<ConfirmUserCommand>
{

    private readonly UsersStorage _usersStorage;
    private readonly ILogger<ConfirmUserCommandHandler> _logger;

    public ConfirmUserCommandHandler(UsersStorage usersStorage, ILogger<ConfirmUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task Handle(ConfirmUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Подтверждение пользователя {UserId}",
            request.UserId
        );

        var user = await _usersStorage.GetById(request.UserId);

        if (user is null)
        {
            _logger.LogWarning("Пользователь {UserId} не найден", request.UserId);
            throw new UserNotFoundException();
        }

        await _usersStorage.ChangeDraftStatus(request.UserId, false);

        _logger.LogInformation(
            "Пользователь {UserId} ({Username}) успешно подтвержден",
            request.UserId,
            user.Username
        );
    }
}