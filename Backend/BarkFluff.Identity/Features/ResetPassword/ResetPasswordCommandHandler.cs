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
    private readonly LocationClient _locationClient;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage,
        AuthPropertiesStorage authPropertiesStorage, UsersServerApi.UsersServerApiClient usersApiClient,
        RequestContext requestContext, NotificationQueueSender notificationQueueSender, LocationClient locationClient,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _resetPasswordsStorage = resetPasswordsStorage;
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersApiClient;
        _requestContext = requestContext;
        _notificationQueueSender = notificationQueueSender;
        _locationClient = locationClient;
        _logger = logger;
    }

    public async Task<ResetPasswordResponse> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        var email = request.Email?.Trim();
        var login = username ?? email;

        _logger.LogInformation(
            "Запрос на сброс пароля для {Login}, тип OTP: {OtpType}",
            login,
            request.OtpType
        );
        if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(email))
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

        if (!string.IsNullOrEmpty(username))
        {
            usersRequest.Username = username;
        }
        else
        {
            usersRequest.Email = email;
        }

        _logger.LogDebug("Поиск пользователя по логину: {Login}", login);

        var user = await _usersClient.FindByLoginAsync(usersRequest);

        if (user.User is null)
        {
            // Защита от энумерации: не раскрываем факт существования пользователя.
            // Why: endpoint сброса пароля не должен позволять перебирать логины/email.
            _logger.LogWarning(
                "Запрос сброса пароля для несуществующего пользователя: {Login}",
                login
            );
            await Task.Delay(Random.Shared.Next(100, 300), cancellationToken);
            return new ResetPasswordResponse { ResetId = Guid.NewGuid().ToString() };
        }

        _logger.LogDebug("Пользователь {UserId} найден, проверка настроек OTP", user.User.Id);

        var enableOtp = await _authPropertiesStorage.CheckOtpEnabled(user.User.Id);

        if (request.OtpType == OtpType.Authenticator && !enableOtp)
        {
            _logger.LogWarning(
                "Запрос сброса пароля с Authenticator OTP, но OTP не настроен для пользователя {UserId}",
                user.User.Id
            );
            throw new OtpNotCreatedException();
        }

        if (request.OtpType == OtpType.Authenticator)
        {
            _logger.LogInformation(
                "Создание запроса на сброс пароля с Authenticator OTP для пользователя {UserId}",
                user.User.Id
            );

            var resetPassword = new Domain.ResetPassword()
            {
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                IsApproved = false,
                OtpType = request.OtpType,
                UserId = user.User.Id
            };

            var resp = await _resetPasswordsStorage.AddResetPassword(resetPassword);

            _logger.LogInformation(
                "Запрос на сброс пароля создан. ResetId: {ResetId}, UserId: {UserId}",
                resp.Id,
                user.User.Id
            );

            return new ResetPasswordResponse { ResetId = resp.Id.ToString() };
        }

        _logger.LogInformation(
            "Создание запроса на сброс пароля с Email OTP для пользователя {UserId}",
            user.User.Id
        );

        var userContactInfo = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = user.User.Id });

        var code = CodeGenerator.GenerateDigitalCode(6);

        _logger.LogDebug("Генерация кода подтверждения для пользователя {UserId}", user.User.Id);

        var resetEmailPassword = new Domain.ResetPassword()
        {
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpCode = code,
            IsApproved = false,
            OtpType = request.OtpType,
            UserId = user.User.Id
        };

        var locationInfo = await _locationClient.GetLocationString(_requestContext.IpAddress);

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
                {"location", locationInfo},
                {"appname", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Код подтверждения для сброса пароля",
            Type = NotificationType.ResetPassword
        };

        resetEmailPassword = await _resetPasswordsStorage.AddResetPassword(resetEmailPassword);

        _logger.LogDebug(
            "Отправка кода подтверждения на адрес {Email} для пользователя {UserId}",
            userContactInfo.Contact.Email,
            user.User.Id
        );

        await _notificationQueueSender.SendNotification(emailNotification);

        _logger.LogInformation(
            "Запрос на сброс пароля создан. ResetId: {ResetId}, UserId: {UserId}, Email: {Email}",
            resetEmailPassword.Id,
            user.User.Id,
            userContactInfo.Contact.Email
        );

        return new ResetPasswordResponse() { ResetId = resetEmailPassword.Id.ToString() };
    }
}