using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using MediatR;

namespace BarkFluff.Identity.Features.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ResetPasswordResponse>
{
    private readonly ResetPasswordsStorage _resetPasswordsStorage;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly RequestContext _requestContext;
    private readonly NotificationQueueSender _notificationQueueSender;

    public ResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage,
        AuthPropertiesStorage authPropertiesStorage, UsersServerApi.UsersServerApiClient usersApiClient,
        RequestContext requestContext, NotificationQueueSender notificationQueueSender)
    {
        _resetPasswordsStorage = resetPasswordsStorage;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersApiClient;
        _requestContext = requestContext;
        _notificationQueueSender = notificationQueueSender;
    }

    public async Task<ResetPasswordResponse> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Username) && string.IsNullOrEmpty(request.Email))
        {
            throw new NotSetUsernameOrEmailException();
        }

        if (string.IsNullOrEmpty(_requestContext.DeviceName))
        {
            throw new XDeviceNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(_requestContext.OperationSystem))
        {
            throw new XOsNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(_requestContext.AppName) || string.IsNullOrEmpty(_requestContext.AppVersion))
        {
            throw new XAppInfoIsRequiedException();
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
        
        var user = await _usersClient.FindByLoginAsync(usersRequest);

        if (user.User is null)
        {
            throw new UserNotFoundException();
        }

        var enableOtp = await _authPropertiesStorage.CheckOtpEnabled(user.User.Id);

        if (request.OtpType == OtpType.Authenticator && !enableOtp)
        {
            throw new OtpNotCreatedException();
        }

        if (request.OtpType == OtpType.Authenticator)
        {
            var resetPassword = new Domain.ResetPassword()
            {
                CreatedAt = DateTime.UtcNow, IsApproved = false, OtpType = request.OtpType, UserId = user.User.Id
            };

            var resp = await _resetPasswordsStorage.AddResetPassword(resetPassword);

            return new ResetPasswordResponse {ResetId = resp.Id.ToString()};
        }
        
        var userContactInfo = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });
        
        var code = CodeGenerator.GenerateDigitalCode(6);
        
        var resetEmailPassword = new Domain.ResetPassword()
        {
            CreatedAt = DateTime.UtcNow, OtpCode = code, IsApproved = false, OtpType = request.OtpType, UserId = user.User.Id
        };
        
        var emailNotification = new EmailNotification()
        {
            OwnerId = user.User.Id,
            Address = userContactInfo.Contact.Email,
            CreatedAt = DateTime.UtcNow,
            Payload = new Dictionary<string, string>()
            {
                {"username", user.User.Username},
                {"confirmation_code", code},
                {"ip", _requestContext.IpAddress ?? string.Empty},
                {"devicename", _requestContext.DeviceName},
                {"os", _requestContext.OperationSystem},
                {"location", "-"},
                {"app", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                {"datetime", DateTime.UtcNow.ToString("D")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Код подтверждения для сброса пароля",
            Type = NotificationType.ResetPassword
        };
        
        resetEmailPassword = await _resetPasswordsStorage.AddResetPassword(resetEmailPassword);
 
        await _notificationQueueSender.SendNotification(emailNotification);

        return new ResetPasswordResponse() { ResetId = resetEmailPassword.Id.ToString() };
    }
}