using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.AddDraftUser;

public class AddDraftUserCommandHandler : IRequestHandler<AddDraftUserCommand, AddDraftUserResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ReservedUsernamesService _reservedUsernamesService;
    private readonly ILogger<AddDraftUserCommandHandler> _logger;

    public AddDraftUserCommandHandler(UsersStorage usersStorage, ReservedUsernamesService reservedUsernamesService, ILogger<AddDraftUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _reservedUsernamesService = reservedUsernamesService;
        _logger = logger;
    }

    public async Task<AddDraftUserResponse> Handle(AddDraftUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Создание черновика пользователя. Username: {Username}, Email: {Email}, Имя: {FirstName} {LastName}",
            request.Username,
            request.Email,
            request.FirstName,
            request.LastName
        );

        var email = request.Email?.Trim();
        var username = request.Username?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        _logger.LogDebug("Проверка существования email: {Email}", email);

        var userByEmail = await _usersStorage.GetUserByEmail(email);

        if (userByEmail != null)
        {
            if (userByEmail.IsDraft)
            {
                _logger.LogWarning(
                    "Email {Email} уже занят черновиком пользователя {UserId}",
                    request.Email,
                    userByEmail.Id
                );
                throw new UserIsDraftException();
            }

            _logger.LogWarning("Email {Email} уже существует у пользователя {UserId}", email, userByEmail.Id);
            throw new EmailExistException();
        }

        _logger.LogDebug("Проверка на зарезервированное имя: {Username}", username);

        if (_reservedUsernamesService.IsReserved(username))
        {
            _logger.LogWarning("Username {Username} является зарезервированным именем", username);
            throw new UsernameReservedException();
        }

        _logger.LogDebug("Проверка существования username: {Username}", username);

        var userByUsername = await _usersStorage.GetUserByUsername(username);

        if (userByUsername != null)
        {
            if (userByUsername.IsDraft)
            {
                _logger.LogWarning(
                    "Username {Username} уже занят черновиком пользователя {UserId}",
                    request.Username,
                    userByUsername.Id
                );
                throw new UserIsDraftException();
            }

            _logger.LogWarning(
                "Username {Username} уже существует у пользователя {UserId}",
                username,
                userByUsername.Id
            );
            throw new UsernameExistException();
        }

        var user = await _usersStorage.CreateUser(username, firstName, lastName, email);

        _logger.LogInformation(
            "Черновик пользователя создан. UserId: {UserId}, Username: {Username}, Email: {Email}",
            user.Id,
            username,
            email
        );

        return new AddDraftUserResponse { UserId = user.Id };
    }
}