using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.CheckExistUsername;

public class CheckExistUsernameQueryHandler : IRequestHandler<CheckExistUsernameQuery, CheckExistResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ReservedUsernamesService _reservedUsernamesService;
    private readonly ILogger<CheckExistUsernameQueryHandler> _logger;

    public CheckExistUsernameQueryHandler(UsersStorage usersStorage, ReservedUsernamesService reservedUsernamesService, ILogger<CheckExistUsernameQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _reservedUsernamesService = reservedUsernamesService;
        _logger = logger;
    }


    public async Task<CheckExistResponse> Handle(CheckExistUsernameQuery request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();

        _logger.LogDebug("Проверка существования username: {Username}", username);

        if (_reservedUsernamesService.IsReserved(username))
        {
            _logger.LogDebug("Username {Username} является зарезервированным именем", username);
            return new CheckExistResponse { Exist = true };
        }

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