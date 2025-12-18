using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.AddDraftUser;

public class AddDraftUserCommandHandler : IRequestHandler<AddDraftUserCommand, AddDraftUserResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ILogger<AddDraftUserCommandHandler> _logger;

    public AddDraftUserCommandHandler(UsersStorage usersStorage, ILogger<AddDraftUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
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

        _logger.LogDebug("Проверка существования email: {Email}", request.Email);

        var userByEmail = await _usersStorage.GetUserByEmail(request.Email);

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

            _logger.LogWarning("Email {Email} уже существует у пользователя {UserId}", request.Email, userByEmail.Id);
            throw new EmailExistException();
        }

        _logger.LogDebug("Проверка существования username: {Username}", request.Username);

        var userByUsername = await _usersStorage.GetUserByUsername(request.Username);

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
                request.Username,
                userByUsername.Id
            );
            throw new UsernameExistException();
        }

        var user = await _usersStorage.CreateUser(request.Username, request.FirstName, request.LastName, request.Email);

        _logger.LogInformation(
            "Черновик пользователя создан. UserId: {UserId}, Username: {Username}, Email: {Email}",
            user.Id,
            request.Username,
            request.Email
        );

        return new AddDraftUserResponse { UserId = user.Id };
    }
}