using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.GetActiveSessions;

public class GetActiveSessionsCommandHandler : IRequestHandler<GetActiveSessionsCommand, GetActiveSessionsResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<GetActiveSessionsCommandHandler> _logger;

    public GetActiveSessionsCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext,
        ILogger<GetActiveSessionsCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<GetActiveSessionsResponse> Handle(GetActiveSessionsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение списка активных сессий для пользователя {UserId}", _userContext.UserId);

        var tokens = await _refreshTokensStorage.GetRefreshTokens(_userContext.UserId);

        _logger.LogInformation(
            "Получено {SessionCount} активных сессий для пользователя {UserId}",
            tokens.Count,
            _userContext.UserId
        );

        return new GetActiveSessionsResponse()
        {
            Sessions =
            {
                tokens
                    .Select(t => new GetActiveSessionsResponse.Types.Session()
                    {
                        CreatedAt = Timestamp.FromDateTime(t.CreatedAt),
                        DeviceName = t.DeviceName,
                        ExpirationAt = Timestamp.FromDateTime(t.ExpiresAt),
                        Id = t.Id
                    })
            }
        };
    }
}