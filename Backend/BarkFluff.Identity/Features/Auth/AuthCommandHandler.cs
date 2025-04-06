using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Identity.Features.Auth;

public class AuthCommandHandler(UsersServerApi.UsersServerApiClient usersClient, 
    IMediator mediator, 
    RefreshTokensStorage refreshTokensStorage) : IRequestHandler<AuthCommand, AuthResponse>
{

    private const int ExpDaysRefreshToken = 9999;
    
    public async Task<AuthResponse> Handle(AuthCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Username) && string.IsNullOrEmpty(request.Email))
        {
            throw new NotSetUsernameOrEmailException();
        }
        
        var usersRequest = new FindByLoginRequest();

        if (string.IsNullOrEmpty(request.Username))
        {
            usersRequest.Username = request.Username;
        }
        else
        {
            usersRequest.Email = request.Email;
        }
        
        var user = await usersClient.FindByLoginAsync(usersRequest);

        if (user.User is null)
        {
            throw new UserNotFoundException();
        }

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        var refreshToken = await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, user.User.Id, request.DeviceName, ExpDaysRefreshToken);

        var accessTokenResponse = await mediator.Send(new CreateTokenCommand() { RefreshToken = refreshTokenString }, cancellationToken);
        
        var response = new AuthResponse() { RefreshToken = new Token 
            {
                Value = refreshTokenString, 
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))
                
            },
            AccessToken = accessTokenResponse.AccessToken
        };

        return response;
    }
}