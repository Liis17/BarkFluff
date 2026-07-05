using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.OverrideDraftUser;

public class OverrideDraftUserCommandHandler : IRequestHandler<OverrideDraftUserCommand, AddDraftUserResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<OverrideDraftUserCommandHandler> _logger;

    public OverrideDraftUserCommandHandler(UsersStorage usersStorage, ILogger<OverrideDraftUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<AddDraftUserResponse> Handle(OverrideDraftUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        var username = request.Username?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        if (UsernameFormatValidator.HasBotSuffix(username))
        {
            _logger.LogWarning("Username {Username} заканчивается на суффикс bot и зарезервирован", username);
            throw new UsernameBotSuffixReservedException();
        }

        _logger.LogInformation(
            "Перезапись черновика пользователя. Username: {Username}, Email: {Email}",
            username,
            email
        );

        var user = await _usersStorage.GetUserByEmail(email)
                   ?? await _usersStorage.GetUserByUsername(username);

        if (user == null)
        {
            _logger.LogWarning(
                "Пользователь не найден по Email {Email} или Username {Username}",
                email,
                username
            );
            throw new UserNotFoundException();
        }

        _logger.LogDebug(
            "Обновление данных черновика пользователя {UserId}",
            user.Id
        );

        user.FirstName = firstName;
        user.LastName = lastName;
        user.Contact!.Email = email; // черновики всегда создаются с контактом (см. UsersStorage.CreateUser)
        user.Username = username;
        user.ProfilePicture = null;
        user.RegistrationDate = DateTime.UtcNow;
        user.IsDraft = true;

        await _usersStorage.UpdateTrackedUser(user);

        _logger.LogInformation(
            "Черновик пользователя {UserId} ({Username}) успешно перезаписан",
            user.Id,
            username
        );

        return new AddDraftUserResponse { UserId = user.Id };
    }
}