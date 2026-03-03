using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.ChangeUsername;

public class ChangeUsernameCommandHandler : IRequestHandler<ChangeUsernameCommand>
{

    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<ChangeUsernameCommandHandler> _logger;


    public ChangeUsernameCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender,
        ILogger<ChangeUsernameCommandHandler> logger)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
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