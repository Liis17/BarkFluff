using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

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
        _logger.LogDebug("Проверка существования email: {Email}", request.Email);

        var userByEmail = await _usersStorage.GetUserByEmail(request.Email);

        if (userByEmail is null || userByEmail.IsDraft)
        {
            _logger.LogDebug("Email {Email} свободен (не найден или черновик)", request.Email);
            return new CheckExistResponse { Exist = false };
        }

        _logger.LogDebug("Email {Email} уже существует у пользователя {UserId}", request.Email, userByEmail.Id);
        return new CheckExistResponse { Exist = true };
    }
}