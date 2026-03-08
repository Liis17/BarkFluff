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

using System.Globalization;

using OtpNotCreatedException = BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException;
using OtpType = BarkFluff.Identity.Domain.OtpType;

namespace BarkFluff.Identity.Features.ConfirmOtpVerification;

public class ConfirmOtpVerificationCommandHandler : IRequestHandler<ConfirmOtpVerificationCommand, ConfirmOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly NotificationQueueSender _notificationQueueSender;
    private readonly RequestContext _requestContext;
    private readonly LocationClient _locationClient;
    private readonly ILogger<ConfirmOtpVerificationCommandHandler> _logger;

    public ConfirmOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        UsersServerApi.UsersServerApiClient usersClient, NotificationQueueSender notificationQueueSender,
        RequestContext requestContext, LocationClient locationClient,
        ILogger<ConfirmOtpVerificationCommandHandler> logger)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _notificationQueueSender = notificationQueueSender;
        _requestContext = requestContext;
        _locationClient = locationClient;
        _logger = logger;
    }

    public async Task<ConfirmOtpVerificationResponse> Handle(ConfirmOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало подтверждения OTP для пользователя {UserId}",
            _userContext.UserId
        );

        string confirmedMethod;
        string oldMethod = "Отключена";

        try
        {
            var otpConfigs = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

            // Определяем предыдущий метод 2FA до активации нового
            if (otpConfigs.OtpEnabled) oldMethod = "Authenticator приложение";
            else if (otpConfigs.EmailOtpEnabled) oldMethod = "Email";

            if (otpConfigs.SelectedOtpType == OtpType.Authenticator)
            {
                _logger.LogDebug("Проверка Authenticator OTP кода для пользователя {UserId}", _userContext.UserId);

                var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(_userContext.UserId);

                var totp = new Totp(Base32Encoding.ToBytes(otpSecret));

                var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

                if (!isValid)
                {
                    _logger.LogWarning(
                        "Неверный Authenticator OTP код для пользователя {UserId}",
                        _userContext.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Активация Authenticator OTP для пользователя {UserId}", _userContext.UserId);

                await _authPropertiesStorage.EnableOtp(_userContext.UserId);
                confirmedMethod = "Authenticator приложение";

                _logger.LogInformation(
                    "Authenticator OTP успешно активирован для пользователя {UserId}",
                    _userContext.UserId
                );
            }
            else if (otpConfigs.SelectedOtpType == OtpType.Email)
            {
                _logger.LogDebug("Проверка Email OTP кода для пользователя {UserId}", _userContext.UserId);

                if (!string.Equals(otpConfigs.LastEmailAuthCode, request.OtpCode,
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogWarning(
                        "Неверный Email OTP код для пользователя {UserId}",
                        _userContext.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Активация Email OTP для пользователя {UserId}", _userContext.UserId);

                await _authPropertiesStorage.EnableEmailOtp(_userContext.UserId);
                confirmedMethod = "Email";

                _logger.LogInformation(
                    "Email OTP успешно активирован для пользователя {UserId}",
                    _userContext.UserId
                );
            }
            else
            {
                return new ConfirmOtpVerificationResponse();
            }
        }
        catch (OtpNotCreatedException ex)
        {
            _logger.LogError(
                ex,
                "OTP не был создан для пользователя {UserId}",
                _userContext.UserId
            );
            throw new BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException();
        }

        // Отправка уведомления об изменении метода 2FA после успешного подтверждения
        await SendTwoFactorChangedNotification(confirmedMethod, oldMethod);

        return new ConfirmOtpVerificationResponse();
    }

    private async Task SendTwoFactorChangedNotification(string newMethod, string oldMethod)
    {
        try
        {
            var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });
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

            var notification = new EmailNotification
            {
                OwnerId = userInfo.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", userInfo.User.Username},
                    {"old_method", oldMethod},
                    {"new_method", newMethod},
                    {"ip", _requestContext.IpAddress ?? string.Empty},
                    {"devicename", _requestContext.DeviceName ?? string.Empty},
                    {"os", _requestContext.OperationSystem ?? string.Empty},
                    {"location", locationInfo},
                    {"datetime", DateTime.UtcNow.ToString("G", CultureInfo.GetCultureInfo("ru-RU"))}
                },
                ServiceId = ServiceId.Identity,
                Title = "Изменен метод двухфакторной аутентификации",
                Type = NotificationType.TwoFactorMethodChanged
            };

            await _notificationQueueSender.SendNotification(notification);

            _logger.LogInformation(
                "Отправлено уведомление об изменении 2FA для пользователя {UserId}, новый метод: {NewMethod}",
                _userContext.UserId,
                newMethod
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при отправке уведомления об изменении 2FA для пользователя {UserId}",
                _userContext.UserId
            );
        }
    }
}