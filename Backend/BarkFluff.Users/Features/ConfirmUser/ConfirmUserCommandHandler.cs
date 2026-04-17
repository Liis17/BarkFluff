using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ConfirmUser;

public class ConfirmUserCommandHandler : IRequestHandler<ConfirmUserCommand>
{

    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly ILogger<ConfirmUserCommandHandler> _logger;

    public ConfirmUserCommandHandler(UsersStorage usersStorage, PrivacyStorage privacyStorage, ILogger<ConfirmUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
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

        // Создаём запись приватности с дефолтами сразу после подтверждения аккаунта.
        // Если запись уже есть (повторный ConfirmUser) — GetOrCreate вернёт существующую.
        await _privacyStorage.GetOrCreate(request.UserId);

        _logger.LogInformation(
            "Пользователь {UserId} ({Username}) успешно подтвержден",
            request.UserId,
            user.Username
        );
    }
}