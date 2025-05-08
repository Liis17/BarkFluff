using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using MassTransit;
using MediatR;

namespace BarkFluff.Identity.Features.CreateAccount;

public class CreateAccountCommandHandler(UsersServerApi.UsersServerApiClient usersClient,
    ConfirmationCodesStorage confirationCodesStorage, NotificationQueueSender notificationQueueSender) 
    : IRequestHandler<CreateAccountCommand, CreateAccountResponse>
{
    public async Task<CreateAccountResponse> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Username))
        {
            throw new UsernameOrEmailIsEmptyException();
        }
        
        var createAccountRequest = new AddDraftUserRequest()
        {
            Email = request.Email,
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName
        };
        
        var responseUser = await usersClient.AddDraftUserAsync(createAccountRequest);

        var code = CodeGenerator.GenerateDigitalCode(6);

        var confirmationCode = new ConfirmationCode()
        {
            Expires = DateTime.UtcNow.AddHours(6),
            OwnerId = responseUser.UserId,
            Type = ConfirmationCodeType.Registration,
            Value = code
        };
        
        confirmationCode = await confirationCodesStorage.AddCode(confirmationCode);

        var payload = new Dictionary<string, string>()
        {
            { "confirmation_code", code },
            { "username", request.Username },
            { "ip", "192.168.1.1" },
            {"devicename", request.DeviceName },
            {"os", "loh"},
            {"location", "Россия 🎉"},
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