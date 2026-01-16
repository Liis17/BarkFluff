using System.Globalization;

namespace BarkFluff.Identity.Features.SetPassword;

using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using GrpcServer.XAuth;
using Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence.Services;
using Services;

public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand>
{
    private readonly UserContext _userContext;
    private readonly PasswordsStorage _passwordsStorage;
    private readonly RefreshTokensStorage refreshTokensStorage;
    private readonly NotificationQueueSender _notificationQueueSender;
    private readonly LocationClient _locationClient;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly RequestContext _requestContext;
    private readonly ILogger<SetPasswordCommandHandler> _logger;

    public SetPasswordCommandHandler(UserContext userContext, PasswordsStorage passwordsStorage,
        RefreshTokensStorage refreshTokensStorage, NotificationQueueSender notificationQueueSender,
        LocationClient locationClient, UsersServerApi.UsersServerApiClient usersClient, RequestContext requestContext,
        ILogger<SetPasswordCommandHandler> logger)
    {
        _userContext = userContext;
        _passwordsStorage = passwordsStorage;
        this.refreshTokensStorage = refreshTokensStorage;
        _notificationQueueSender = notificationQueueSender;
        _locationClient = locationClient;
        _usersClient = usersClient;
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало изменения пароля для пользователя {UserId}",
            _userContext.UserId
        );

        var passwordHash = PasswordHasher.HashPassword(request.NewPassword);

        _logger.LogDebug("Обновление хэша пароля в БД для пользователя {UserId}", _userContext.UserId);

        var isNewUser = await _passwordsStorage.UpdateUserPasswordHash(_userContext.UserId, passwordHash);

        // Отправка уведомления об изменении пароля
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

        if (!isNewUser)
        {
            var passwordChangedNotification = new EmailNotification
            {
                OwnerId = _userContext.UserId,
                Address = userContacts.Contact.Email,
                CreatedAt = DateTime.UtcNow,
                Payload = new Dictionary<string, string>
                {
                    {"username", userInfo.User.Username},
                    {"ip", _requestContext.IpAddress ?? string.Empty},
                    {"devicename", _requestContext.DeviceName ?? string.Empty},
                    {"os", _requestContext.OperationSystem ?? string.Empty},
                    {"location", locationInfo},
                    {"appname", $"{_requestContext.AppName} v.{_requestContext.AppVersion}"},
                    {"datetime", DateTime.UtcNow.ToString("G", CultureInfo.GetCultureInfo("ru-RU"))}

                },
                ServiceId = ServiceId.Identity,
                Title = "Пароль успешно изменен",
                Type = NotificationType.PasswordChanged
            };

            _logger.LogDebug(
                "Отправка уведомления об изменении пароля на адрес {Email}",
                userContacts.Contact.Email
            );

            await _notificationQueueSender.SendNotification(passwordChangedNotification);
        }

        _logger.LogInformation(
            "Пароль успешно изменен для пользователя {UserId}. Уведомление отправлено",
            _userContext.UserId
        );
    }
}