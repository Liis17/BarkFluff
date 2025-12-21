using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.ChangeName;

public class ChangeNameCommandHandler : IRequestHandler<ChangeNameCommand>
{
    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<ChangeNameCommandHandler> _logger;

    public ChangeNameCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender,
        ILogger<ChangeNameCommandHandler> logger)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
        _logger = logger;
    }

    public async Task Handle(ChangeNameCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало изменения имени для пользователя {UserId}: '{FirstName} {LastName}'",
            _userContext.UserId,
            request.FirstName,
            request.LastName
        );

        await _usersStorage.ChangeName(_userContext.UserId, request.FirstName, request.LastName);

        _logger.LogDebug(
            "Отправка события об изменении имени в очередь RabbitMQ для пользователя {UserId}",
            _userContext.UserId
        );

        await _userInfoQueueSender.NameChangedEvent(_userContext.UserId, request.FirstName, request.LastName);

        _logger.LogInformation(
            "Изменение имени успешно завершено для пользователя {UserId}",
            _userContext.UserId
        );
    }
}