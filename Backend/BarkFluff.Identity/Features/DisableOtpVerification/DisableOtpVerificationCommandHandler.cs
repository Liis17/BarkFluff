using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
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
    private readonly MetricsCollector _metrics;
    private readonly ILogger<DisableOtpVerificationCommandHandler> _logger;
    private readonly IIdentityAbuseGuard _abuseGuard;

    public DisableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        NotificationQueueSender notificationQueueSender, LocationClient locationClient,
        UsersServerApi.UsersServerApiClient usersClient, RequestContext requestContext,
        MetricsCollector metrics, ILogger<DisableOtpVerificationCommandHandler> logger,
        IIdentityAbuseGuard abuseGuard)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _notificationQueueSender = notificationQueueSender;
        _locationClient = locationClient;
        _usersClient = usersClient;
        _requestContext = requestContext;
        _metrics = metrics;
        _logger = logger;
        _abuseGuard = abuseGuard;
    }

    public async Task<DisableOtpVerificationResponse> Handle(DisableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало отключения 2FA для пользователя {UserId}, тип: {OtpType}",
            _userContext.UserId,
            request.OptType
        );

        await _abuseGuard.EnsureOtpOperationAllowedAsync(
            IdentityOtpOperation.Disable,
            _userContext.UserId,
            cancellationToken);

        var otpConfigs = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

        if (otpConfigs is null)
        {
            _logger.LogWarning(
                "Попытка отключить 2FA для пользователя {UserId}, но настройки не найдены",
                _userContext.UserId
            );
            throw new OtpNotCreatedException();
        }

        string oldMethod = "Неизвестно";
        if (request.OptType == OtpTypeId.Authenticator)
        {
            if (!otpConfigs.OtpEnabled)
            {
                _logger.LogWarning(
                    "Попытка отключить Authenticator 2FA для пользователя {UserId}, но он не включен",
                    _userContext.UserId
                );
                throw new OtpNotCreatedException();
            }

            oldMethod = "Authenticator приложение";

            _logger.LogDebug("Проверка OTP кода для отключения Authenticator 2FA");

            if (string.IsNullOrWhiteSpace(request.OtpCode))
                throw new OtpCodeNeedException();

            var totp = new Totp(Base32Encoding.ToBytes(otpConfigs.OtpSecret));

            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                var failure = await _abuseGuard.RegisterOtpFailureAsync(
                    IdentityOtpOperation.Disable,
                    _userContext.UserId,
                    cancellationToken);
                await _abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

                _metrics.Increment("otp_authenticator_failed");
                _metrics.Increment("otp_disable_failed");
                _logger.LogWarning(
                    "Неверный OTP код при попытке отключения Authenticator 2FA для пользователя {UserId}",
                    _userContext.UserId
                );

                if (failure.Locked)
                    throw new IdentityLockoutException();

                throw new NotValidOtpCodeException();
            }

            _logger.LogDebug("Отключение Authenticator 2FA для пользователя {UserId}", _userContext.UserId);

            await _authPropertiesStorage.DisableOtp(_userContext.UserId);
            _metrics.Increment("otp_disabled_authenticator");
        }

        if (request.OptType == OtpTypeId.Email)
        {
            _logger.LogDebug("Отключение Email 2FA для пользователя {UserId}", _userContext.UserId);

            oldMethod = "Email";
            await _authPropertiesStorage.DisableEmailOtp(_userContext.UserId);
            _metrics.Increment("otp_disabled_email");
        }

        await _abuseGuard.ClearOtpFailuresAsync(
            IdentityOtpOperation.Disable,
            _userContext.UserId,
            cancellationToken);

        // Отправка уведомления об изменении метода 2FA (отключении)
        var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });
        var userContacts = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = _userContext.UserId });

        var locationInfo = await _locationClient.GetLocationString(_requestContext.IpAddress);

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
                {"appname", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}

            },
            ServiceId = ServiceId.Identity,
            Title = "Изменен метод двухфакторной аутентификации",
            Type = NotificationType.TwoFactorMethodChanged
        };

        _logger.LogDebug(
            "Отправка уведомления об отключении 2FA на адрес {Email}",
            userContacts.Contact.Email
        );

        await _notificationQueueSender.SendNotification(twoFactorChangedNotification);

        _logger.LogInformation(
            "2FA успешно отключена для пользователя {UserId}. Метод: {OldMethod}",
            _userContext.UserId,
            oldMethod
        );

        return new DisableOtpVerificationResponse();
    }
}
