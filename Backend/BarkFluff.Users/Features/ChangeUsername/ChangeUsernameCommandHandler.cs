using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChangeUsername;

public class ChangeUsernameCommandHandler : IRequestHandler<ChangeUsernameCommand>
{

    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly ReservedUsernamesService _reservedUsernamesService;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<ChangeUsernameCommandHandler> _logger;


    public ChangeUsernameCommandHandler(UserContext userContext, UsersStorage usersStorage, ReservedUsernamesService reservedUsernamesService,
        UserInfoQueueSender userInfoQueueSender, ILogger<ChangeUsernameCommandHandler> logger)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _reservedUsernamesService = reservedUsernamesService;
        _userInfoQueueSender = userInfoQueueSender;
        _logger = logger;
    }

    public async Task Handle(ChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();

        _logger.LogInformation(
            "Начало изменения username для пользователя {UserId}: '{Username}'",
            _userContext.UserId,
            username
        );

        if (!UsernameFormatValidator.IsValid(username))
        {
            _logger.LogWarning("Username {Username} имеет недопустимый формат", username);
            throw new UsernameInvalidFormatException();
        }

        if (UsernameFormatValidator.HasBotSuffix(username))
        {
            _logger.LogWarning("Username {Username} заканчивается на суффикс bot и зарезервирован", username);
            throw new UsernameBotSuffixReservedException();
        }

        if (_reservedUsernamesService.IsReserved(username))
        {
            _logger.LogWarning("Username {Username} является зарезервированным именем", username);
            throw new UsernameReservedException();
        }

        var existingUser = await _usersStorage.GetUserByUsername(username);
        if (existingUser != null && existingUser.Id != _userContext.UserId)
        {
            _logger.LogWarning("Username {Username} уже занят пользователем {UserId}", username, existingUser.Id);
            throw new UsernameExistException();
        }

        await _usersStorage.ChangeUsername(_userContext.UserId, username);

        _logger.LogDebug(
            "Отправка события об изменении username в очередь RabbitMQ для пользователя {UserId}",
            _userContext.UserId
        );

        await _userInfoQueueSender.UsernameChangedEvent(_userContext.UserId, username);

        _logger.LogInformation(
            "Username успешно изменен для пользователя {UserId}",
            _userContext.UserId
        );
    }
}