using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Identity.Features.CreateSessionForUserServer;

public class CreateSessionForUserServerCommandHandler(
    UsersServerApi.UsersServerApiClient usersClient,
    IMediator mediator,
    NotificationQueueSender notificationQueueSender,
    RefreshTokensStorage refreshTokensStorage,
    LocationClient locationClient,
    MetricsCollector metrics,
    ILogger<CreateSessionForUserServerCommandHandler> logger)
    : IRequestHandler<CreateSessionForUserServerCommand, CreateSessionForUserServerResponse>
{
    private const int ExpDaysRefreshToken = 9999;

    public async Task<CreateSessionForUserServerResponse> Handle(CreateSessionForUserServerCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId is required"));
        }

        if (string.IsNullOrEmpty(request.DeviceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DeviceId is required"));
        }

        if (!Guid.TryParse(request.DeviceId, out var parsedDeviceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DeviceId must be a valid GUID"));
        }

        var deviceId = parsedDeviceId.ToString();

        if (string.IsNullOrEmpty(request.DeviceName))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DeviceName is required"));
        }

        if (string.IsNullOrEmpty(request.OperationSystem))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "OperationSystem is required"));
        }

        if (string.IsNullOrEmpty(request.AppName))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "AppName is required"));
        }

        var user = await usersClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId });
        if (user.User.IsBot)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Bot accounts cannot create user sessions"));
        }

        logger.LogInformation(
            "Создание серверной сессии для пользователя {UserId} на устройстве {DeviceId} ({DeviceName})",
            request.UserId, deviceId, request.DeviceName);

        await refreshTokensStorage.DeleteRefreshTokensByDeviceIdSafe(deviceId, request.UserId);

        var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
        await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, request.UserId, deviceId, ExpDaysRefreshToken);

        var accessTokenResponse = await mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

        var locationInfo = await locationClient.GetLocationString(request.IpAddress);

        try
        {
            await usersClient.RegisterDeviceAsync(new RegisterDeviceRequest
            {
                DeviceId = deviceId,
                UserId = request.UserId,
                OriginalName = request.DeviceName,
                AppName = request.AppName,
                OperationSystem = request.OperationSystem,
                Location = locationInfo
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось зарегистрировать устройство {DeviceId} для пользователя {UserId}",
                deviceId, request.UserId);
        }

        try
        {
            var userContacts = await usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = request.UserId });

            var notification = new EmailNotification
            {
                OwnerId = request.UserId,
                Address = userContacts.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", userContacts.User.Username},
                    {"ip", request.IpAddress},
                    {"devicename", request.DeviceName},
                    {"os", request.OperationSystem},
                    {"location", locationInfo},
                    {"appname", request.AppName},
                    {"datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")}
                },
                ServiceId = ServiceId.Identity,
                Title = "Успешный вход в аккаунт",
                Type = NotificationType.SuccessfulLogin
            };

            await notificationQueueSender.SendNotification(notification);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Не удалось отправить уведомление об успешном входе для пользователя {UserId}",
                request.UserId);
        }

        metrics.Increment("server_sessions_created");
        metrics.Increment("sessions_created");

        logger.LogInformation(
            "Серверная сессия успешно создана для пользователя {UserId}, устройство {DeviceId}",
            request.UserId, deviceId);

        return new CreateSessionForUserServerResponse
        {
            AccessToken = accessTokenResponse.AccessToken,
            RefreshToken = new Token
            {
                Value = refreshTokenString,
                ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken))
            }
        };
    }
}
