using BarkFluff.GrpcServer.Metrics;
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
    private readonly MetricsCollector _metrics;
    private readonly ILogger<EnableOtpVerificationCommandHandler> _logger;

    public EnableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        UsersServerApi.UsersServerApiClient usersClient, NotificationQueueSender notificationQueueSender,
        RequestContext requestContext, LocationClient locationClient, MetricsCollector metrics,
        ILogger<EnableOtpVerificationCommandHandler> logger)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _notificationQueueSender = notificationQueueSender;
        _requestContext = requestContext;
        _locationClient = locationClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<EnableOtpVerificationResponse> Handle(EnableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало включения 2FA для пользователя {UserId}, тип: {OtpType}",
            _userContext.UserId,
            request.OptType
        );
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
            _logger.LogDebug("Настройка Authenticator 2FA для пользователя {UserId}", _userContext.UserId);

            // Получаем старый метод 2FA
            var oldOptOptions = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);
            string oldMethod = "Отключена";
            if (oldOptOptions != null)
            {
                if (oldOptOptions.OtpEnabled) oldMethod = "Authenticator приложение";
                else if (oldOptOptions.EmailOtpEnabled) oldMethod = "Email";
            }

            _logger.LogDebug("Предыдущий метод 2FA: {OldMethod}", oldMethod);

            var key = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(key);

            var optUri = new OtpUri(OtpType.Totp, base32Secret, userInfo.User.Username, "BarkFluff");

            var uri = optUri.ToString();

            _logger.LogDebug("Генерация QR кода для Authenticator 2FA");

            await _authPropertiesStorage.AddUserOtpSecretKey(_userContext.UserId, base32Secret);
            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Authenticator, userInfo.User.Id);

            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);

            var qrCode = new Base64QRCode(qrCodeData);

            var qrCodeBase64 = qrCode.GetGraphic(20);

            _metrics.Increment("otp_setup_authenticator");

            _logger.LogInformation(
                "Authenticator 2FA успешно настроен для пользователя {UserId}. Старый метод: {OldMethod}",
                _userContext.UserId,
                oldMethod
            );

            return new EnableOtpVerificationResponse()
            {
                OtpQr = qrCodeBase64,
                OtpCode = base32Secret
            };
        }

        if (request.OptType == OtpTypeId.Email)
        {
            _logger.LogDebug("Настройка Email 2FA для пользователя {UserId}", _userContext.UserId);
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

            _logger.LogDebug("Генерация кода подтверждения для Email 2FA");

            await _authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);

            await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Email, userContactInfo.User.Id);

            var locationInfo = await _locationClient.GetLocationString(_requestContext.IpAddress);

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
                    {"appname", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                    {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Код подтверждения для привязки",
                Type = NotificationType.ConfirmationOtpEmail
            };

            _logger.LogDebug(
                "Отправка кода подтверждения на адрес {Email}",
                userContactInfo.Contact.Email
            );

            await _notificationQueueSender.SendNotification(emailNotification);

            _metrics.Increment("otp_setup_email");
            _metrics.Increment("otp_email_codes_sent");

            _logger.LogInformation(
                "Email 2FA успешно настроен для пользователя {UserId}. Старый метод: {OldMethod}",
                _userContext.UserId,
                oldMethod
            );

            return new EnableOtpVerificationResponse() { OtpQr = string.Empty };
        }

        _logger.LogWarning(
            "Неизвестный тип OTP: {OtpType} для пользователя {UserId}",
            request.OptType,
            _userContext.UserId
        );

        return new EnableOtpVerificationResponse() { OtpQr = string.Empty }; ;
    }
}