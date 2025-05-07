using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Identity.Features.GetActiveSessions;

public class GetActiveSessionsCommandHandler : IRequestHandler<GetActiveSessionsCommand, GetActiveSessionsResponse>
{

    private readonly RefreshTokensStorage _refreshTokensStorage;

    private readonly UserContext _userContext;

    public GetActiveSessionsCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
    }

    public async Task<GetActiveSessionsResponse> Handle(GetActiveSessionsCommand request, CancellationToken cancellationToken)
    {
        var tokens = await _refreshTokensStorage.GetRefreshTokens(_userContext.UserId);

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