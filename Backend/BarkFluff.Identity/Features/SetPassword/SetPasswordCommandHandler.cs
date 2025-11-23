namespace BarkFluff.Identity.Features.SetPassword;

using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using GrpcServer.XAuth;
using Infrastructure;
using MediatR;
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

    public SetPasswordCommandHandler(UserContext userContext, PasswordsStorage passwordsStorage,
        RefreshTokensStorage refreshTokensStorage, NotificationQueueSender notificationQueueSender,
        LocationClient locationClient, UsersServerApi.UsersServerApiClient usersClient, RequestContext requestContext)
    {
        _userContext = userContext;
        _passwordsStorage = passwordsStorage;
        this.refreshTokensStorage = refreshTokensStorage;
        _notificationQueueSender = notificationQueueSender;
        _locationClient = locationClient;
        _usersClient = usersClient;
        _requestContext = requestContext;
    }

    public async Task Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var passwordHash = PasswordHasher.HashPassword(request.NewPassword);

        await _passwordsStorage.UpdateUserPasswordHash(_userContext.UserId, passwordHash);

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
                {"datetime", DateTime.UtcNow.ToString("D")}
            },
            ServiceId = ServiceId.Identity,
            Title = "Пароль успешно изменен",
            Type = NotificationType.PasswordChanged
        };

        await _notificationQueueSender.SendNotification(passwordChangedNotification);
    }
}