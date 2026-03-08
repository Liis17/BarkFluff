using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChangeBio;

public class ChangeBioCommandHandler : IRequestHandler<ChangeBioCommand>
{

    private readonly UserContext _userContext;
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<ChangeBioCommandHandler> _logger;


    public ChangeBioCommandHandler(UserContext userContext, UsersStorage usersStorage, UserInfoQueueSender userInfoQueueSender,
        ILogger<ChangeBioCommandHandler> logger)
    {
        _userContext = userContext;
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
        _logger = logger;
    }

    public async Task Handle(ChangeBioCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало изменения биографии для пользователя {UserId}. Длина: {BioLength}",
            _userContext.UserId,
            request.Bio?.Length ?? 0
        );

        if (request.Bio != null && request.Bio.Length > 200)
        {
            _logger.LogWarning(
                "Биография для пользователя {UserId} слишком длинная: {BioLength} символов (макс. 200)",
                _userContext.UserId,
                request.Bio.Length
            );
            throw new BioTooLongException();
        }

        await _usersStorage.ChangeBio(_userContext.UserId, request.Bio);

        _logger.LogDebug(
            "Отправка события об изменении биографии в очередь RabbitMQ для пользователя {UserId}",
            _userContext.UserId
        );

        await _userInfoQueueSender.UserBioChangedEvent(_userContext.UserId, request.Bio);

        _logger.LogInformation(
            "Биография успешно изменена для пользователя {UserId}",
            _userContext.UserId
        );
    }
}