using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using MediatR;
using OtpNet;
using QRCoder;

namespace BarkFluff.Identity.Features.EnableOtpVerification;

public class EnableOtpVerificationCommandHandler : IRequestHandler<EnableOtpVerificationCommand, EnableOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient _usersClient;
    private readonly NotificationQueueSender _notificationQueueSender;
    private readonly RequestContext _requestContext;

    public EnableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage, 
        UsersServerApi.UsersServerApiClient usersClient, NotificationQueueSender notificationQueueSender, 
        RequestContext requestContext)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _notificationQueueSender = notificationQueueSender;
        _requestContext = requestContext;
    }

    public async Task<EnableOtpVerificationResponse> Handle(EnableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
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
        
        var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest() { UserId = _userContext.UserId });

        if (request.OptType == OtpTypeId.Authenticator)
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(key);

            var optUri = new OtpUri(OtpType.Totp, base32Secret, userInfo.User.Username, "BarkFluff");

            var uri = optUri.ToString();
            
            await _authPropertiesStorage.AddUserOtpSecretKey(_userContext.UserId, base32Secret);
            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Authenticator, userInfo.User.Id);
            
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        
            var qrCode = new Base64QRCode(qrCodeData);

            var qrCodeBase64 = qrCode.GetGraphic(20);

            return new EnableOtpVerificationResponse()
            {
                OtpQr = qrCodeBase64,
                OtpCode = base32Secret
            };
        }

        if (request.OptType == OtpTypeId.Email)
        {
            var userContactInfo = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = _userContext.UserId });

            var code = CodeGenerator.GenerateDigitalCode(6);
            
            await _authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);
            
            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Email, userContactInfo.User.Id);
            
            var emailNotification = new EmailNotification()
            {
                OwnerId = userInfo.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>()
                {
                    {"username", userInfo.User.Username},
                    {"confirmation_code", code},
                    {"ip", _requestContext.IpAddress ?? string.Empty},
                    {"devicename", _requestContext.DeviceName},
                    {"os", _requestContext.OperationSystem},
                    {"location", "-"},
                    {"app", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                    {"datetime", DateTime.UtcNow.ToString("D")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Код подтверждения для привязки",
                Type = NotificationType.ConfirmationOtpEmail
            };

            await _notificationQueueSender.SendNotification(emailNotification);

            return new EnableOtpVerificationResponse() { OtpQr = string.Empty };
        }


        return new EnableOtpVerificationResponse() { OtpQr = string.Empty };;
    }
}