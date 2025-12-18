using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.RemoveActiveSession;

public class RemoveActiveSessionCommandHandler: IRequestHandler<RemoveActiveSessionCommand, RemoveActiveSessionResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<RemoveActiveSessionCommandHandler> _logger;

    public RemoveActiveSessionCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext,
        ILogger<RemoveActiveSessionCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<RemoveActiveSessionResponse> Handle(RemoveActiveSessionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Попытка удаления сессии {SessionId} для пользователя {UserId}",
            request.Id,
            _userContext.UserId
        );

        try
        {
            await _refreshTokensStorage.DeleteRefreshToken(request.Id, _userContext.UserId);

            _logger.LogInformation(
                "Сессия {SessionId} успешно удалена для пользователя {UserId}",
                request.Id,
                _userContext.UserId
            );
        }
        catch (RefreshTokenNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Сессия {SessionId} не найдена для пользователя {UserId}",
                request.Id,
                _userContext.UserId
            );
            throw new SessionNotFoundException();
        }

        return new RemoveActiveSessionResponse();
    }
}