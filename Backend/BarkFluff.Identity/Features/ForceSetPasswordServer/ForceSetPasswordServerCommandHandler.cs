using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;

using MediatR;

namespace BarkFluff.Identity.Features.ForceSetPasswordServer;

public class ForceSetPasswordServerCommandHandler : IRequestHandler<ForceSetPasswordServerCommand, ForceSetPasswordServerResponse>
{
    private readonly PasswordsStorage _passwordsStorage;
    private readonly NotificationQueueSender _notificationQueueSender;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<ForceSetPasswordServerCommandHandler> _logger;

    public ForceSetPasswordServerCommandHandler(
        PasswordsStorage passwordsStorage,
        NotificationQueueSender notificationQueueSender,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<ForceSetPasswordServerCommandHandler> logger)
    {
        _passwordsStorage = passwordsStorage;
        _notificationQueueSender = notificationQueueSender;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<ForceSetPasswordServerResponse> Handle(ForceSetPasswordServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Принудительная смена пароля для пользователя {UserId} (admin)", request.UserId);

        var passwordHash = PasswordHasher.HashPassword(request.NewPassword);
        await _passwordsStorage.UpdateUserPasswordHash(request.UserId, passwordHash);

        _logger.LogInformation("Пароль успешно изменён для пользователя {UserId} (admin)", request.UserId);

        try
        {
            var userInfo = await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId });
            var userContacts = await _usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = request.UserId });

            if (!string.IsNullOrEmpty(userContacts.Contact?.Email))
            {
                var notification = new EmailNotification
                {
                    OwnerId = request.UserId,
                    Address = userContacts.Contact.Email,
                    CreatedAt = DateTime.UtcNow,
                    Payload = new Dictionary<string, string>
                    {
                        { "username", userInfo.User?.Username ?? string.Empty },
                        { "adminusername", "AdminPanel" },
                        { "datetime", DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss") }
                    },
                    ServiceId = ServiceId.Identity,
                    Title = "Пароль изменён администратором",
                    Type = NotificationType.PasswordChangedByAdmin
                };

                await _notificationQueueSender.SendNotification(notification);
                _logger.LogInformation("Email-уведомление о смене пароля отправлено пользователю {UserId}", request.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить email-уведомление пользователю {UserId}", request.UserId);
        }

        return new ForceSetPasswordServerResponse();
    }
}
