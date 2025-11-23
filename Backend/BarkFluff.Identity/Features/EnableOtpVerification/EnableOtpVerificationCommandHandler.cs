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
    private readonly LocationClient _locationClient;

    public EnableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage, 
        UsersServerApi.UsersServerApiClient usersClient, NotificationQueueSender notificationQueueSender, 
        RequestContext requestContext, LocationClient locationClient)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _notificationQueueSender = notificationQueueSender;
        _requestContext = requestContext;
        _locationClient = locationClient;
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
            // Получаем старый метод 2FA
            var oldOptOptions = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);
            string oldMethod = "Отключена";
            if (oldOptOptions != null)
            {
                if (oldOptOptions.OtpEnabled) oldMethod = "Authenticator приложение";
                else if (oldOptOptions.EmailOtpEnabled) oldMethod = "Email";
            }

            var key = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(key);

            var optUri = new OtpUri(OtpType.Totp, base32Secret, userInfo.User.Username, "BarkFluff");

            var uri = optUri.ToString();

            await _authPropertiesStorage.AddUserOtpSecretKey(_userContext.UserId, base32Secret);
            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Authenticator, userInfo.User.Id);

            // Отправка уведомления об изменении метода 2FA
            var userContactInfo = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = _userContext.UserId });

            string locationInfo = "-";
            if (!string.IsNullOrEmpty(_requestContext.IpAddress))
            {
                var ipLocation = await _locationClient.GetLocation(_requestContext.IpAddress);
                if (ipLocation != null)
                {
                    locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
                }
            }

            var twoFactorChangedNotification = new EmailNotification
            {
                OwnerId = userInfo.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", userInfo.User.Username},
                    {"old_method", oldMethod},
                    {"new_method", "Authenticator приложение"},
                    {"ip", _requestContext.IpAddress ?? string.Empty},
                    {"devicename", _requestContext.DeviceName},
                    {"os", _requestContext.OperationSystem},
                    {"location", locationInfo},
                    {"datetime", DateTime.UtcNow.ToString("D")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Изменен метод двухфакторной аутентификации",
                Type = NotificationType.TwoFactorMethodChanged
            };

            await _notificationQueueSender.SendNotification(twoFactorChangedNotification);

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
            // Получаем старый метод 2FA
            var oldOptOptions = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);
            string oldMethod = "Отключена";
            if (oldOptOptions != null)
            {
                if (oldOptOptions.OtpEnabled) oldMethod = "Authenticator приложение";
                else if (oldOptOptions.EmailOtpEnabled) oldMethod = "Email";
            }

            var userContactInfo = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = _userContext.UserId });

            var code = CodeGenerator.GenerateDigitalCode(6);

            await _authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);

            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Email, userContactInfo.User.Id);

            // Получаем данные о местоположении IP-адреса
            string locationInfo = "-";
            if (!string.IsNullOrEmpty(_requestContext.IpAddress))
            {
                var ipLocation = await _locationClient.GetLocation(_requestContext.IpAddress);
                if (ipLocation != null)
                {
                    locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
                }
            }

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
                    {"location", locationInfo},
                    {"app", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                    {"datetime", DateTime.UtcNow.ToString("D")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Код подтверждения для привязки",
                Type = NotificationType.ConfirmationOtpEmail
            };

            await _notificationQueueSender.SendNotification(emailNotification);

            // Отправка уведомления об изменении метода 2FA
            var twoFactorChangedNotification = new EmailNotification
            {
                OwnerId = userInfo.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", userInfo.User.Username},
                    {"old_method", oldMethod},
                    {"new_method", "Email"},
                    {"ip", _requestContext.IpAddress ?? string.Empty},
                    {"devicename", _requestContext.DeviceName},
                    {"os", _requestContext.OperationSystem},
                    {"location", locationInfo},
                    {"datetime", DateTime.UtcNow.ToString("D")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Изменен метод двухфакторной аутентификации",
                Type = NotificationType.TwoFactorMethodChanged
            };

            await _notificationQueueSender.SendNotification(twoFactorChangedNotification);

            return new EnableOtpVerificationResponse() { OtpQr = string.Empty };
        }


        return new EnableOtpVerificationResponse() { OtpQr = string.Empty };;
    }
}