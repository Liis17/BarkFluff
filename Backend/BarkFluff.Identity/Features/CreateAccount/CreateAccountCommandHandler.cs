using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using MassTransit;
using MediatR;

namespace BarkFluff.Identity.Features.CreateAccount;

public class CreateAccountCommandHandler(UsersServerApi.UsersServerApiClient usersClient,
    ConfirmationCodesStorage confirationCodesStorage, NotificationQueueSender notificationQueueSender,
    RequestContext requestContext, LocationClient locationClient) 
    : IRequestHandler<CreateAccountCommand, CreateAccountResponse>
{
    public async Task<CreateAccountResponse> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Username))
        {
            throw new UsernameOrEmailIsEmptyException();
        }

        if (string.IsNullOrEmpty(requestContext.DeviceName))
        {
            throw new XDeviceNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(requestContext.OperationSystem))
        {
            throw new XOsNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(requestContext.AppName) || string.IsNullOrEmpty(requestContext.AppVersion))
        {
            throw new XAppInfoIsRequiedException();
        }
        
        var createAccountRequest = new AddDraftUserRequest()
        {
            Email = request.Email,
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        AddDraftUserResponse responseUser = null;

        try
        {
            responseUser = await usersClient.AddDraftUserAsync(createAccountRequest);
        }
        catch (UserIsDraftException)
        {
            responseUser = await usersClient.OverrideDraftUserAsync(createAccountRequest);
        }

        var code = CodeGenerator.GenerateDigitalCode(6);

        var confirmationCode = new ConfirmationCode()
        {
            Expires = DateTime.UtcNow.AddHours(6),
            OwnerId = responseUser.UserId,
            Type = ConfirmationCodeType.Registration,
            Value = code
        };
        
        confirmationCode = await confirationCodesStorage.AddCode(confirmationCode);

        // Получаем данные о местоположении IP-адреса
        string locationInfo = "-";
        if (!string.IsNullOrEmpty(requestContext.IpAddress))
        {
            var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);
            if (ipLocation != null)
            {
                locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
            }
        }

        var payload = new Dictionary<string, string>()
        {
            { "confirmation_code", code },
            { "username", request.Username },
            { "ip", requestContext.IpAddress ?? string.Empty },
            {"devicename", requestContext.DeviceName },
            {"os", requestContext.OperationSystem},
            {"location", locationInfo},
            {"app", $"{requestContext.AppName} v.{requestContext.AppVersion}"},
            {"datetime", DateTime.UtcNow.ToString("F")}
        };
        
        await notificationQueueSender.SendNotification(new EmailNotification()
        {
            Address = request.Email,
            CreatedAt = DateTime.UtcNow,
            OwnerId = responseUser.UserId,
            ServiceId = ServiceId.Identity,
            Payload = payload,
            Title = "Код подтверждения",
            Type = NotificationType.ConfirmationRegistration,
        });
        
        return new CreateAccountResponse { CodeId = confirmationCode.Id.ToString()};
    }
}