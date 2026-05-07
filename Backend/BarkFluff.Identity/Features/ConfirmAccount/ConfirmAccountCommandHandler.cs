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

using Google.Protobuf.WellKnownTypes;

using MediatR;


namespace BarkFluff.Identity.Features.ConfirmAccount;

public class ConfirmAccountCommandHandler(ConfirmationCodesStorage confirmationCodesStorage,
    UsersServerApi.UsersServerApiClient usersClient, RefreshTokensStorage refreshTokensStorage, RequestContext requestContext,
    NotificationQueueSender notificationQueueSender, LocationClient locationClient, ILogger<ConfirmAccountCommandHandler> logger)
    : IRequestHandler<ConfirmAccountCommand, ConfirmAccountResponse>
{

    private const int ExpDaysRefreshToken = 9999;


    public async Task<ConfirmAccountResponse> Handle(ConfirmAccountCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Начало подтверждения аккаунта. CodeId: {CodeId}",
            request.CodeId
        );
        if (string.IsNullOrEmpty(requestContext.DeviceName))
        {
            throw new XDeviceNameIsRequiredException();
        }

        var codeId = Guid.Parse(request.CodeId);

        logger.LogDebug("Получение кода подтверждения {CodeId}", codeId);

        var code = await confirmationCodesStorage.GetCode(codeId);

        if (code is null)
        {
            logger.LogWarning("Код подтверждения {CodeId} не найден", codeId);
            throw new ConfirmationCodeNotFoundException();
        }

        if (code.Type != ConfirmationCodeType.Registration)
        {
            // Why: защита от использования кода другого типа на endpoint подтверждения регистрации.
            logger.LogWarning(
                "Код подтверждения {CodeId} имеет неожиданный тип {Type}, ожидается Registration",
                codeId,
                code.Type
            );
            throw new ConfirmationCodeNotFoundException();
        }

        if (code.Expires < DateTime.UtcNow)
        {
            logger.LogWarning(
                "Код подтверждения {CodeId} истек. Истек: {ExpirationDate}",
                codeId,
                code.Expires
            );
            throw new ConfirmationCodeExpiredException();
        }

        var equals = code.Value.Equals(request.Code, StringComparison.InvariantCultureIgnoreCase);

        if (!equals)
        {
            logger.LogWarning(
                "Неверный код подтверждения для CodeId {CodeId}, UserId {UserId}",
                codeId,
                code.OwnerId
            );
            throw new ConfirmationCodeIncorrectException();
        }

        logger.LogDebug("Подтверждение пользователя {UserId}", code.OwnerId!.Value);

        var confirmRequest = new ConfirmUserRequest { UserId = code.OwnerId!.Value };

        await usersClient.ConfirmUserAsync(confirmRequest);

        // Удаляем использованный код, чтобы предотвратить повторное использование.
        await confirmationCodesStorage.DeleteCode(codeId);

        // Отправка уведомления об успешной регистрации
        var userInfo = await usersClient.GetByIdAsync(new GetByIdRequest { UserId = code.OwnerId!.Value });
        var userContacts = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = code.OwnerId!.Value });

        var locationInfo = await locationClient.GetLocationString(requestContext.IpAddress);

        var successfulRegistrationNotification = new EmailNotification
        {
            OwnerId = code.OwnerId!.Value,
            Address = userContacts.Contact.Email,
            CreatedAt = DateTime.UtcNow,
            Payload = new Dictionary<string, string>
            {
                {"username", userInfo.User.Username},
                {"ip", requestContext.IpAddress ?? string.Empty},
                {"devicename", requestContext.DeviceName},
                {"os", requestContext.OperationSystem ?? string.Empty},
                {"location", locationInfo},
                {"appname", $"{requestContext.AppName} v.{requestContext.AppVersion}"},
                {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Успешная регистрация",
            Type = NotificationType.SuccessfulRegistration
        };

        logger.LogDebug(
            "Отправка уведомления об успешной регистрации на адрес {Email}",
            userContacts.Contact.Email
        );

        await notificationQueueSender.SendNotification(successfulRegistrationNotification);

        logger.LogDebug("Генерация refresh token для пользователя {UserId}", code.OwnerId!.Value);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();

        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, code.OwnerId!.Value, requestContext.DeviceId ?? requestContext.DeviceName, ExpDaysRefreshToken);

        logger.LogInformation(
            "Аккаунт успешно подтвержден. UserId: {UserId}, Username: {Username}, Устройство: {DeviceName}",
            code.OwnerId!.Value,
            userInfo.User.Username,
            requestContext.DeviceName
        );

        return new ConfirmAccountResponse()
        {
            RefreshToken = new Token
            {
                Value = refreshTokenString,
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))
            }
        };
    }
}