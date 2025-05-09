using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using OtpNet;

namespace BarkFluff.Identity.Features.Auth;

public class AuthCommandHandler(UsersServerApi.UsersServerApiClient usersClient, 
    IMediator mediator, AuthPropertiesStorage authPropertiesStorage, NotificationQueueSender notificationQueueSender,
    UserContext userContext, RefreshTokensStorage refreshTokensStorage) : IRequestHandler<AuthCommand, AuthResponse>
{

    private const int ExpDaysRefreshToken = 9999;
    
    public async Task<AuthResponse> Handle(AuthCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Username) && string.IsNullOrEmpty(request.Email))
        {
            throw new NotSetUsernameOrEmailException();
        }
        
        var usersRequest = new FindByLoginRequest();

        if (!string.IsNullOrEmpty(request.Username))
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

        var optOptions = await authPropertiesStorage.GetUserAuthProperties(user.User.Id);

        if (optOptions != null && (optOptions.EmailOtpEnabled || optOptions.OtpEnabled) && string.IsNullOrEmpty(request.OtpCode))
        {
            if (optOptions is { OtpEnabled: false, EmailOtpEnabled: true })
            {
                var userContactInfo = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = userContext.UserId });
                var code = CodeGenerator.GenerateDigitalCode(6);
                
                await authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);
            
                var emailNotification = new EmailNotification
                {
                    OwnerId = userContactInfo.User.Id,
                    Address = userContactInfo.Contact.Email,
                    CreatedAt = DateTime.UtcNow,
                    Payload = new Dictionary<string, string>
                    {
                        {"username", userContactInfo.User.Username},
                        {"confirmation_code", code},
                        {"ip", "192.168.1.1"},
                        {"devicename", request.DeviceName},
                        {"os", "loh"},
                        {"location", "Россия 😊"},
                        {"datetime", DateTime.UtcNow.ToString("D")}
                    },
                    ServiceId = ServiceId.Identity,
                    Title = "Код подтверждения для входа",
                    Type = NotificationType.ConfirmationAuth
                };

                await notificationQueueSender.SendNotification(emailNotification);
            }
            
            throw new OtpCodeNeedException();
        }

        if (optOptions is { OtpEnabled: true })
        {
            var otpSecret = optOptions.OtpSecret;
            
            var totp = new Totp(Base32Encoding.ToBytes(otpSecret));
            
            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                throw new NotValidOtpCodeException();
            }
        }

        if (optOptions is { OtpEnabled: false, EmailOtpEnabled: true })
        {
            if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotValidOtpCodeException();
            }
        }

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, user.User.Id, request.DeviceName, ExpDaysRefreshToken);

        var accessTokenResponse = await mediator.Send(new CreateTokenCommand() { RefreshToken = refreshTokenString }, cancellationToken);
        
        var response = new AuthResponse { RefreshToken = new Token 
            {
                Value = refreshTokenString, 
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))
                
            },
            AccessToken = accessTokenResponse.AccessToken
        };

        return response;
    }
}