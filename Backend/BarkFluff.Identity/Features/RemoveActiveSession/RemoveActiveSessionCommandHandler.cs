using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.RemoveActiveSession;

public class RemoveActiveSessionCommandHandler: IRequestHandler<RemoveActiveSessionCommand, RemoveActiveSessionResponse>
{

    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;

    public RemoveActiveSessionCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
    }

    public async Task<RemoveActiveSessionResponse> Handle(RemoveActiveSessionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await _refreshTokensStorage.DeleteRefreshToken(request.Id, _userContext.UserId);
        }
        catch (RefreshTokenNotFoundException)
        {
            throw new SessionNotFoundException();
        }
        
        return new RemoveActiveSessionResponse();
    }
}