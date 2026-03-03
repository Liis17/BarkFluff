using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.FindByLogin;

public class FindByLoginQueryHandler : IRequestHandler<FindByLoginQuery, FindByLoginResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ILogger<FindByLoginQueryHandler> _logger;

    public FindByLoginQueryHandler(UsersStorage usersStorage, ILogger<FindByLoginQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<FindByLoginResponse> Handle(FindByLoginQuery request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        var email = request.Email?.Trim();

        _logger.LogDebug(
            "Поиск пользователя по логину. Username: {Username}, Email: {Email}",
            username ?? "null",
            email ?? "null"
        );

        Domain.User? user = null;
        if (!string.IsNullOrEmpty(username))
        {
            user = await _usersStorage.GetUserByUsername(username);
        }

        if (!string.IsNullOrEmpty(email))
        {
            user = await _usersStorage.GetUserByEmail(email);
        }

        if (user is null)
        {
            _logger.LogWarning(
                "Пользователь не найден. Username: {Username}, Email: {Email}",
                username ?? "null",
                email ?? "null"
            );
            throw new UserNotFoundException();
        }

        _logger.LogInformation(
            "Пользователь найден. UserId: {UserId}, Username: {Username}",
            user.Id,
            user.Username
        );

        return new FindByLoginResponse { User = user.ToGrpc() };
    }
}