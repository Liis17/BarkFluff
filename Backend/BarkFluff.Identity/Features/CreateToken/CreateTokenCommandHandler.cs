using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.CreateToken;

public class CreateTokenCommandHandler(RefreshTokensStorage refreshTokensStorage, JwtService jwtService) 
    : IRequestHandler<CreateTokenCommand, CreateTokenResponse>
{
    public async Task<CreateTokenResponse> Handle(CreateTokenCommand request, CancellationToken cancellationToken)
    {
        var accessToken = await refreshTokensStorage.FindRefreshToken(request.RefreshToken);

        if (accessToken == null
            || accessToken.ExpiresAt < DateTime.UtcNow
            || string.IsNullOrEmpty(accessToken.DeviceName))
        {
            throw new InvalidRefreshTokenException();
        }

        var token = jwtService.GenerateUserToken(accessToken.UserId);
        
        return new CreateTokenResponse { AccessToken = token, };
    }
}