using BarkFluff.GrpcServer.Tracker;
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

namespace BarkFluff.Identity.Features.ConfirmAccount;

public class ConfirmAccountCommandHandler(ConfirmationCodesStorage confirmationCodesStorage,
    UsersServerApi.UsersServerApiClient usersClient, RefreshTokensStorage refreshTokensStorage, RequestContext requestContext,
    NotificationQueueSender notificationQueueSender, LocationClient locationClient)
    : IRequestHandler<ConfirmAccountCommand, ConfirmAccountResponse>
{
    
    private const int ExpDaysRefreshToken = 9999;

    
    public async Task<ConfirmAccountResponse> Handle(ConfirmAccountCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(requestContext.DeviceName))
        {
            throw new XDeviceNameIsRequiredException();
        } 
        
        var codeId = Guid.Parse(request.CodeId);

        var code = await confirmationCodesStorage.GetCode(codeId);

        if (code is null)
        {
            throw new ConfirmationCodeNotFoundException();
        }

        if (code.Expires < DateTime.UtcNow)
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

        // Отправка уведомления об успешной регистрации
        var userInfo = await usersClient.GetByIdAsync(new GetByIdRequest { UserId = code.OwnerId!.Value });
        var userContacts = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = code.OwnerId!.Value });

        string locationInfo = "-";
        if (!string.IsNullOrEmpty(requestContext.IpAddress))
        {
            var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);
            if (ipLocation != null)
            {
                locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
            }
        }

        var successfulRegistrationNotification = new EmailNotification
        {
            OwnerId = code.OwnerId!.Value,
            Address = userContacts.Contact.Email,
            CreatedAt = DateTime.UtcNow,
            Payload = new Dictionary<string, string>
            {
                {"username", userInfo.User.Username},
                {"ip", requestContext.IpAddress ?? string.Empty},
                {"devicename", requestContext.DeviceName},
                {"os", requestContext.OperationSystem ?? string.Empty},
                {"location", locationInfo},
                {"datetime", DateTime.UtcNow.ToString("D")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Успешная регистрация",
            Type = NotificationType.SuccessfulRegistration
        };

        await notificationQueueSender.SendNotification(successfulRegistrationNotification);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();

        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, code.OwnerId!.Value, requestContext.DeviceName, ExpDaysRefreshToken);

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