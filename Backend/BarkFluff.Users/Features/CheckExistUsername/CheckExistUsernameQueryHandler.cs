using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.CheckExistUsername;

public class CheckExistUsernameQueryHandler : IRequestHandler<CheckExistUsernameQuery, CheckExistResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<CheckExistUsernameQueryHandler> _logger;

    public CheckExistUsernameQueryHandler(UsersStorage usersStorage, ILogger<CheckExistUsernameQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }


    public async Task<CheckExistResponse> Handle(CheckExistUsernameQuery request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();

        _logger.LogDebug("Проверка существования username: {Username}", username);

        var userByUsername = await _usersStorage.GetUserByUsername(username);

        if (userByUsername is null || userByUsername.IsDraft)
        {
            _logger.LogDebug("Username {Username} свободен (не найден или черновик)", username);
            return new CheckExistResponse { Exist = false };
        }

        _logger.LogDebug("Username {Username} уже существует у пользователя {UserId}", username, userByUsername.Id);
        return new CheckExistResponse() { Exist = true };
    }
}