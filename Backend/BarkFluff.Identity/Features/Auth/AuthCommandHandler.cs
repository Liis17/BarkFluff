using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Google.Protobuf.WellKnownTypes;

using MediatR;

using OtpNet;


namespace BarkFluff.Identity.Features.Auth;

public class AuthCommandHandler(UsersServerApi.UsersServerApiClient usersClient,
    IMediator mediator, AuthPropertiesStorage authPropertiesStorage, NotificationQueueSender notificationQueueSender,
    RefreshTokensStorage refreshTokensStorage, RequestContext requestContext, PasswordsStorage passwordsStorage,
    LocationClient locationClient, ILogger<AuthCommandHandler> logger) : IRequestHandler<AuthCommand, AuthResponse>
{

    private const int ExpDaysRefreshToken = 9999;

    public async Task<AuthResponse> Handle(AuthCommand request, CancellationToken cancellationToken)
    {
        var login = request.Username ?? request.Email;

        logger.LogInformation(
            "Попытка входа: {Login} с устройства {DeviceName}",
            login,
            requestContext.DeviceName
        );

        if (string.IsNullOrEmpty(request.Username) && string.IsNullOrEmpty(request.Email))
        {
            logger.LogWarning("Попытка входа без указания логина или email");
            throw new NotSetUsernameOrEmailException();
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            logger.LogWarning("Попытка входа без пароля для {Login}", login);
            throw new InvalidLoginOrPasswordException();
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

        // Если DeviceId не передан, генерируем временный для обратной совместимости
        var deviceId = string.IsNullOrEmpty(requestContext.DeviceId)
            ? Guid.NewGuid().ToString()
            : requestContext.DeviceId;

        var usersRequest = new FindByLoginRequest();

        if (!string.IsNullOrEmpty(request.Username))
        {
            usersRequest.Username = request.Username;
        }
        else
        {
            usersRequest.Email = request.Email;
        }

        logger.LogDebug("Поиск пользователя по логину: {Login}", login);

        var user = await usersClient.FindByLoginAsync(usersRequest);

        if (user.User is null)
        {
            logger.LogWarning(
                "Неудачная попытка входа: пользователь не найден. Логин: {Login}, IP: {IpAddress}",
                login,
                requestContext.IpAddress
            );
            throw new InvalidLoginOrPasswordException();
        }

        var optOptions = await authPropertiesStorage.GetUserAuthProperties(user.User.Id);

        if (optOptions != null && (optOptions.EmailOtpEnabled || optOptions.OtpEnabled) && string.IsNullOrEmpty(request.OtpCode))
        {
            logger.LogInformation(
                "Требуется OTP код для пользователя {UserId}. Email OTP: {EmailOtp}, App OTP: {AppOtp}",
                user.User.Id,
                optOptions.EmailOtpEnabled,
                optOptions.OtpEnabled
            );

            if (optOptions is { OtpEnabled: false, EmailOtpEnabled: true })
            {
                logger.LogDebug("Генерация и отправка Email OTP кода для пользователя {UserId}", user.User.Id);

                var userContactInfo = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });
                var code = CodeGenerator.GenerateDigitalCode(6);

                await authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);

                // Получаем данные о местоположении IP-адреса
                var locationInfo = await locationClient.GetLocationString(requestContext.IpAddress);

                var emailNotification = new EmailNotification
                {
                    OwnerId = userContactInfo.User.Id,
                    Address = userContactInfo.Contact.Email,
                    CreatedAt = DateTime.UtcNow,
                    Payload = new Dictionary<string, string>
                    {
                        {"username", userContactInfo.User.Username},
                        {"confirmation_code", code},
                        {"ip", requestContext.IpAddress ?? string.Empty},
                        {"devicename", requestContext.DeviceName},
                        {"os", requestContext.OperationSystem},
                        {"location", locationInfo},
                        {"appname", $"{requestContext.AppName} v.{requestContext.AppVersion}"},
                        {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                    },
                    ServiceId = ServiceId.Identity,
                    Title = "Код подтверждения для входа",
                    Type = NotificationType.ConfirmationAuth
                };

                await notificationQueueSender.SendNotification(emailNotification);
            }

            throw new OtpCodeNeedException();
        }

        if (optOptions is { OtpEnabled: true })
        {
            logger.LogDebug("Проверка TOTP кода для пользователя {UserId}", user.User.Id);

            var otpSecret = optOptions.OtpSecret;

            var totp = new Totp(Base32Encoding.ToBytes(otpSecret));

            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                logger.LogWarning(
                    "Неверный TOTP код для пользователя {UserId}, IP: {IpAddress}",
                    user.User.Id,
                    requestContext.IpAddress
                );
                throw new NotValidOtpCodeException();
            }

            logger.LogDebug("TOTP код успешно проверен для пользователя {UserId}", user.User.Id);
        }

        if (optOptions is { OtpEnabled: false, EmailOtpEnabled: true })
        {
            logger.LogDebug("Проверка Email OTP кода для пользователя {UserId}", user.User.Id);

            if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                logger.LogWarning(
                    "Неверный Email OTP код для пользователя {UserId}, IP: {IpAddress}",
                    user.User.Id,
                    requestContext.IpAddress
                );
                throw new NotValidOtpCodeException();
            }

            logger.LogDebug("Email OTP код успешно проверен для пользователя {UserId}", user.User.Id);
        }

        logger.LogDebug("Проверка пароля для пользователя {UserId}", user.User.Id);

        var currentPasswordHash = await passwordsStorage.GetUserPasswordHash(user.User.Id);

        if (!PasswordHasher.VerifyPassword(request.Password, currentPasswordHash))
        {
            logger.LogWarning(
                "Неудачная попытка входа: неверный пароль для пользователя {UserId}. Логин: {Login}, IP: {IpAddress}",
                user.User.Id,
                login,
                requestContext.IpAddress
            );

            // Отправка уведомления о неудачной попытке входа
            var userContactInfo = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });

            var locationInfo = await locationClient.GetLocationString(requestContext.IpAddress);

            var failedLoginNotification = new EmailNotification
            {
                OwnerId = user.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", user.User.Username},
                    {"ip", requestContext.IpAddress ?? string.Empty},
                    {"devicename", requestContext.DeviceName},
                    {"os", requestContext.OperationSystem},
                    {"location", locationInfo},
                    {"appname", $"{requestContext.AppName} v.{requestContext.AppVersion}"},
                    {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Неуспешная попытка входа в аккаунт",
                Type = NotificationType.FailedLogin
            };

            await notificationQueueSender.SendNotification(failedLoginNotification);

            throw new InvalidLoginOrPasswordException();
        }

        logger.LogInformation(
            "Успешная аутентификация пользователя {UserId} ({Login}) с устройства {DeviceName}, IP: {IpAddress}",
            user.User.Id,
            login,
            requestContext.DeviceName,
            requestContext.IpAddress
        );

        logger.LogDebug("Генерация refresh token для пользователя {UserId}", user.User.Id);

        // Удаляем старые токены для этого устройства перед созданием нового
        await refreshTokensStorage.DeleteRefreshTokensByDeviceIdSafe(deviceId, user.User.Id);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, user.User.Id, deviceId, ExpDaysRefreshToken);

        var accessTokenResponse = await mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

        // Регистрация устройства в Users сервисе
        var successLocationInfo = await locationClient.GetLocationString(requestContext.IpAddress);

        try
        {
            await usersClient.RegisterDeviceAsync(new RegisterDeviceRequest
            {
                DeviceId = deviceId,
                UserId = user.User.Id,
                OriginalName = requestContext.DeviceName ?? "Unknown",
                AppName = $"{requestContext.AppName} v.{requestContext.AppVersion}",
                OperationSystem = requestContext.OperationSystem ?? "",
                Location = successLocationInfo
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось зарегистрировать устройство {DeviceId} для пользователя {UserId}",
                deviceId, user.User.Id);
        }

        // Отправка уведомления об успешном входе
        var successUserContactInfo = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });

        var successfulLoginNotification = new EmailNotification
        {
            OwnerId = user.User.Id,
            Address = successUserContactInfo.Contact.Email,
            CreatedAt = DateTime.UtcNow,
            Payload = new Dictionary<string, string>
            {
                {"username", user.User.Username},
                {"ip", requestContext.IpAddress ?? string.Empty},
                {"devicename", requestContext.DeviceName},
                {"os", requestContext.OperationSystem},
                {"location", successLocationInfo},
                {"appname", $"{requestContext.AppName} v.{requestContext.AppVersion}"},
                {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Успешный вход в аккаунт",
            Type = NotificationType.SuccessfulLogin
        };

        await notificationQueueSender.SendNotification(successfulLoginNotification);

        logger.LogInformation(
            "Аутентификация завершена успешно для пользователя {UserId}. Токены сгенерированы, уведомление отправлено",
            user.User.Id
        );

        var response = new AuthResponse
        {
            RefreshToken = new Token
            {
                Value = refreshTokenString,
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))

            },
            AccessToken = accessTokenResponse.AccessToken
        };

        return response;
    }
}