using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

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
        _logger.LogInformation(
            "Перезапись черновика пользователя. Username: {Username}, Email: {Email}",
            request.Username,
            request.Email
        );

        var user = await _usersStorage.GetUserByEmail(request.Email)
                   ?? await _usersStorage.GetUserByUsername(request.Username);

        if (user == null)
        {
            _logger.LogWarning(
                "Пользователь не найден по Email {Email} или Username {Username}",
                request.Email,
                request.Username
            );
            throw new UserNotFoundException();
        }

        _logger.LogDebug(
            "Обновление данных черновика пользователя {UserId}",
            user.Id
        );

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Contact.Email = request.Email;
        user.Username = request.Username;
        user.ProfilePicture = null;
        user.RegistrationDate = DateTime.UtcNow;
        user.IsDraft = true;

        await _usersStorage.UpdateTrackedUser(user);

        _logger.LogInformation(
            "Черновик пользователя {UserId} ({Username}) успешно перезаписан",
            user.Id,
            request.Username
        );

        return new AddDraftUserResponse { UserId = user.Id };
    }
}