using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
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

    public EnableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage, 
        UsersServerApi.UsersServerApiClient usersClient, NotificationQueueSender notificationQueueSender)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _notificationQueueSender = notificationQueueSender;
    }

    public async Task<EnableOtpVerificationResponse> Handle(EnableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest() { UserId = _userContext.UserId });

        if (request.OptType == OtpTypeId.Authenticator)
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(key);
            
            var uri = new OtpUri(OtpType.Totp, base32Secret, userInfo.User.Username, "BarkFluff").ToString();
        
            await _authPropertiesStorage.AddUserOtpSecretKey(_userContext.UserId, base32Secret);
            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Authenticator, userInfo.User.Id);

        
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        
            var qrCode = new Base64QRCode(qrCodeData);

            var qrCodeBase64 = qrCode.GetGraphic(20);

            return new EnableOtpVerificationResponse()
            {
                OtpQr = qrCodeBase64
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
                    {"ip", "192.168.1.1"},
                    {"devicename", request.DeviceName},
                    {"os", "loh"},
                    {"location", "Россия 😊"},
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