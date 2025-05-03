using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Identity.Features.ConfirmAccount;

public class ConfirmAccountCommandHandler(ConfirmationCodesStorage confirmationCodesStorage,
    UsersServerApi.UsersServerApiClient usersClient, RefreshTokensStorage refreshTokensStorage) 
    : IRequestHandler<ConfirmAccountCommand, ConfirmAccountResponse>
{
    
    private const int ExpDaysRefreshToken = 9999;

    
    public async Task<ConfirmAccountResponse> Handle(ConfirmAccountCommand request, CancellationToken cancellationToken)
    {
        var codeId = Guid.Parse(request.CodeId);

        var code = await confirmationCodesStorage.GetCode(codeId);

        if (code is null)
        {
            throw new ConfirmationCodeNotFoundException();
        }

        if (code.Expires > DateTime.UtcNow)
        {
            throw new ConfirmationCodeExpiredException();
        }

        var equals = code.Value.Equals(request.Code, StringComparison.InvariantCultureIgnoreCase);

        if (!equals)
        {
            throw new ConfirmationCodeIncorrectException();
        }

        var confirmRequest = new ConfirmUserRequest { UserId = code.OwnerId!.Value };

        await usersClient.ConfirmUserAsync(confirmRequest);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        
        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, code.OwnerId!.Value, request.DeviceName, ExpDaysRefreshToken);

        return new ConfirmAccountResponse()
        {
            RefreshToken = new Token
            {
                Value = refreshTokenString,
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))
            }
        };
    }
}