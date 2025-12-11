using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using MediatR;
using OtpNet;
using OtpNotCreatedException = BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException;

namespace BarkFluff.Identity.Features.DisableOtpVerification;

public class DisableOtpVerificationCommandHandler : IRequestHandler<DisableOtpVerificationCommand, DisableOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly NotificationQueueSender _notificationQueueSender;
    private readonly LocationClient _locationClient;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly RequestContext _requestContext;

    public DisableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        NotificationQueueSender notificationQueueSender, LocationClient locationClient,
        UsersServerApi.UsersServerApiClient usersClient, RequestContext requestContext)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _notificationQueueSender = notificationQueueSender;
        _locationClient = locationClient;
        _usersClient = usersClient;
        _requestContext = requestContext;
    }

    public async Task<DisableOtpVerificationResponse> Handle(DisableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        var otpConfigs = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

        if (otpConfigs is null)
        {
            throw new OtpNotCreatedException();
        }

        string oldMethod = "Неизвестно";
        if (request.OptType == OtpTypeId.Authenticator)
        {
            if (!otpConfigs.OtpEnabled)
            {
                throw new OtpNotCreatedException();
            }

            oldMethod = "Authenticator приложение";

            var totp = new Totp(Base32Encoding.ToBytes(otpConfigs.OtpSecret));

            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                throw new NotValidOtpCodeException();
            }

            await _authPropertiesStorage.DisableOtp(_userContext.UserId);
        }

        if (request.OptType == OtpTypeId.Email)
        {
            oldMethod = "Email";
            await _authPropertiesStorage.DisableEmailOtp(_userContext.UserId);
        }

        // Отправка уведомления об изменении метода 2FA (отключении)
        var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });
        var userContacts = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = _userContext.UserId });

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
            OwnerId = _userContext.UserId,
            Address = userContacts.Contact.Email,
            CreatedAt = DateTime.UtcNow,
            Payload = new Dictionary<string, string>
            {
                {"username", userInfo.User.Username},
                {"old_method", oldMethod},
                {"new_method", "Отключена"},
                {"ip", _requestContext.IpAddress ?? string.Empty},
                {"devicename", _requestContext.DeviceName ?? string.Empty},
                {"os", _requestContext.OperationSystem ?? string.Empty},
                {"location", locationInfo},
                {"datetime", DateTime.UtcNow.ToString("D")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Изменен метод двухфакторной аутентификации",
            Type = NotificationType.TwoFactorMethodChanged
        };

        await _notificationQueueSender.SendNotification(twoFactorChangedNotification);

        return new DisableOtpVerificationResponse();
    }
}