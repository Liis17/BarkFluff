using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
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
    LocationClient locationClient, MetricsCollector metrics, ILogger<AuthCommandHandler> logger,
    IIdentityAbuseGuard abuseGuard) : IRequestHandler<AuthCommand, AuthResponse>
{

    private const int ExpDaysRefreshToken = 9999;
    private const string DevelopersPortalAppName = "BarkFluff Developers Portal";

    public async Task<AuthResponse> Handle(AuthCommand request, CancellationToken cancellationToken)
    {
        var login = string.IsNullOrWhiteSpace(request.Username)
            ? request.Email?.Trim()
            : request.Username.Trim();

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

        var ipAddress = string.IsNullOrWhiteSpace(requestContext.TrustedIpAddress)
            ? requestContext.IpAddress
            : requestContext.TrustedIpAddress;
        var appName = FormatAppName(requestContext.AppName, requestContext.AppVersion);

        await abuseGuard.EnsureLoginAllowedAsync(
            login!,
            requestContext.TrustedIpAddress,
            cancellationToken);

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
            var failure = await abuseGuard.RegisterLoginFailureAsync(
                login!,
                requestContext.TrustedIpAddress,
                null,
                cancellationToken);
            await abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

            metrics.Increment("auth_login_failed");
            metrics.Increment("auth_login_failed_user_not_found");
            logger.LogWarning(
                "Неудачная попытка входа: пользователь не найден. Логин: {Login}, IP: {IpAddress}",
                login,
                ipAddress
            );

            if (failure.Locked)
                throw new IdentityLockoutException();

            throw new InvalidLoginOrPasswordException();
        }

        if (user.User.IsBot)
        {
            metrics.Increment("auth_login_failed");
            logger.LogWarning("Попытка пользовательского входа для бота {UserId}", user.User.Id);
            throw new InvalidLoginOrPasswordException();
        }

        await abuseGuard.EnsureUserAllowedAsync(user.User.Id, cancellationToken);

        var optOptions = await authPropertiesStorage.GetUserAuthProperties(user.User.Id);

        if (optOptions != null && (optOptions.EmailOtpEnabled || optOptions.OtpEnabled) && string.IsNullOrWhiteSpace(request.OtpCode))
        {
            await abuseGuard.EnsureSubjectRequestAllowedAsync(
                IdentityAbuseOperation.Auth,
                login!,
                cancellationToken);

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
                var locationInfo = await locationClient.GetLocationString(ipAddress);

                var emailNotification = new EmailNotification
                {
                    OwnerId = userContactInfo.User.Id,
                    Address = userContactInfo.Contact.Email,
                    CreatedAt = DateTime.UtcNow,
                    Payload = new Dictionary<string, string>
                    {
                        {"username", userContactInfo.User.Username},
                        {"confirmation_code", code},
                        {"ip", ipAddress ?? string.Empty},
                        {"devicename", requestContext.DeviceName},
                        {"os", requestContext.OperationSystem},
                        {"location", locationInfo},
                        {"appname", appName},
                        {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                    },
                    ServiceId = ServiceId.Identity,
                    Title = "Код подтверждения для входа",
                    Type = NotificationType.ConfirmationAuth
                };

                await notificationQueueSender.SendNotification(emailNotification);
                metrics.Increment("otp_email_codes_sent");
            }

            metrics.Increment("auth_otp_required");
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
                var failure = await abuseGuard.RegisterLoginFailureAsync(
                    login!,
                    requestContext.TrustedIpAddress,
                    user.User.Id,
                    cancellationToken);
                await abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

                metrics.Increment("auth_login_failed");
                metrics.Increment("otp_authenticator_failed");
                logger.LogWarning(
                    "Неверный TOTP код для пользователя {UserId}, IP: {IpAddress}",
                    user.User.Id,
                    ipAddress
                );

                if (failure.Locked)
                    throw new IdentityLockoutException();

                throw new NotValidOtpCodeException();
            }

            metrics.Increment("otp_authenticator_verified");
            logger.LogDebug("TOTP код успешно проверен для пользователя {UserId}", user.User.Id);
        }

        if (optOptions is { OtpEnabled: false, EmailOtpEnabled: true })
        {
            logger.LogDebug("Проверка Email OTP кода для пользователя {UserId}", user.User.Id);

            if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                var failure = await abuseGuard.RegisterLoginFailureAsync(
                    login!,
                    requestContext.TrustedIpAddress,
                    user.User.Id,
                    cancellationToken);
                await abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

                metrics.Increment("auth_login_failed");
                metrics.Increment("otp_email_failed");
                logger.LogWarning(
                    "Неверный Email OTP код для пользователя {UserId}, IP: {IpAddress}",
                    user.User.Id,
                    ipAddress
                );

                if (failure.Locked)
                    throw new IdentityLockoutException();

                throw new NotValidOtpCodeException();
            }

            metrics.Increment("otp_email_verified");
            logger.LogDebug("Email OTP код успешно проверен для пользователя {UserId}", user.User.Id);
        }

        logger.LogDebug("Проверка пароля для пользователя {UserId}", user.User.Id);

        var currentPasswordHash = await passwordsStorage.GetUserPasswordHash(user.User.Id);

        if (!PasswordHasher.VerifyPassword(request.Password, currentPasswordHash))
        {
            var failure = await abuseGuard.RegisterLoginFailureAsync(
                login!,
                requestContext.TrustedIpAddress,
                user.User.Id,
                cancellationToken);
            await abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

            metrics.Increment("auth_login_failed");
            metrics.Increment("auth_login_failed_invalid_password");
            logger.LogWarning(
                "Неудачная попытка входа: неверный пароль для пользователя {UserId}. Логин: {Login}, IP: {IpAddress}",
                user.User.Id,
                login,
                ipAddress
            );

            if (failure.Locked)
                throw new IdentityLockoutException();

            // Отправка уведомления о неудачной попытке входа
            var userContactInfo = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });

            var locationInfo = await locationClient.GetLocationString(ipAddress);

            var failedLoginNotification = new EmailNotification
            {
                OwnerId = user.User.Id,
                Address = userContactInfo.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", user.User.Username},
                    {"ip", ipAddress ?? string.Empty},
                    {"devicename", requestContext.DeviceName},
                    {"os", requestContext.OperationSystem},
                    {"location", locationInfo},
                    {"appname", appName},
                    {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Неуспешная попытка входа в аккаунт",
                Type = NotificationType.FailedLogin
            };

            await notificationQueueSender.SendNotification(failedLoginNotification);

            throw new InvalidLoginOrPasswordException();
        }

        await abuseGuard.ClearLoginFailuresAsync(
            login!,
            requestContext.TrustedIpAddress,
            user.User.Id,
            cancellationToken);

        logger.LogInformation(
            "Успешная аутентификация пользователя {UserId} ({Login}) с устройства {DeviceName}, IP: {IpAddress}",
            user.User.Id,
            login,
            requestContext.DeviceName,
            ipAddress
        );

        logger.LogDebug("Генерация refresh token для пользователя {UserId}", user.User.Id);

        // Удаляем старые токены для этого устройства перед созданием нового
        await refreshTokensStorage.DeleteRefreshTokensByDeviceIdSafe(deviceId, user.User.Id);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, user.User.Id, deviceId, ExpDaysRefreshToken);

        var accessTokenResponse = await mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

        // Регистрация устройства в Users сервисе
        var successLocationInfo = await locationClient.GetLocationString(ipAddress);

        try
        {
            await usersClient.RegisterDeviceAsync(new RegisterDeviceRequest
            {
                DeviceId = deviceId,
                UserId = user.User.Id,
                OriginalName = requestContext.DeviceName ?? "Unknown",
                AppName = appName,
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
                {"ip", ipAddress ?? string.Empty},
                {"devicename", requestContext.DeviceName},
                {"os", requestContext.OperationSystem},
                {"location", successLocationInfo},
                {"appname", appName},
                {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Успешный вход в аккаунт",
            Type = NotificationType.SuccessfulLogin
        };

        await notificationQueueSender.SendNotification(successfulLoginNotification);

        metrics.Increment("auth_login_success");
        metrics.Increment("sessions_created");

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

    private static string FormatAppName(string? appName, string? appVersion)
    {
        return string.Equals(appName, DevelopersPortalAppName, StringComparison.Ordinal)
            ? DevelopersPortalAppName
            : $"{appName} v.{appVersion}";
    }
}
