using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.CheckExistEmail;

public class CheckExistEmailQueryHandler : IRequestHandler<CheckExistEmailQuery, CheckExistResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ILogger<CheckExistEmailQueryHandler> _logger;

    public CheckExistEmailQueryHandler(UsersStorage usersStorage, ILogger<CheckExistEmailQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<CheckExistResponse> Handle(CheckExistEmailQuery request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();

        _logger.LogDebug("Проверка существования email: {Email}", email);

        var userByEmail = await _usersStorage.GetUserByEmail(email);

        if (userByEmail is null || userByEmail.IsDraft)
        {
            _logger.LogDebug("Email {Email} свободен (не найден или черновик)", email);
            return new CheckExistResponse { Exist = false };
        }

        _logger.LogDebug("Email {Email} уже существует у пользователя {UserId}", email, userByEmail.Id);
        return new CheckExistResponse { Exist = true };
    }
}