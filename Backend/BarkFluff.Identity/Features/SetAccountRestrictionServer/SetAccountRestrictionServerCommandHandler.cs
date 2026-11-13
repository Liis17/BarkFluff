using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Identity;
using BarkFluff.Shared.Queue.Notifications;
using BarkFluff.Shared.Queue.Users;
using BarkFluff.Shared.Identity;

using MassTransit;

using MediatR;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Features.SetAccountRestrictionServer;

public sealed class SetAccountRestrictionServerCommandHandler(
    UsersServerApi.UsersServerApiClient usersClient,
    IdentityContext identityContext,
    RefreshTokensStorage refreshTokensStorage,
    NotificationQueueSender notificationQueueSender,
    IPublishEndpoint publishEndpoint,
    JwtSettings jwtSettings,
    ILogger<SetAccountRestrictionServerCommandHandler> logger)
    : IRequestHandler<SetAccountRestrictionServerCommand, SetAccountRestrictionServerResponse>
{
    public async Task<SetAccountRestrictionServerResponse> Handle(
        SetAccountRestrictionServerCommand request, CancellationToken cancellationToken)
    {
        var changed = await usersClient.SetAccountRestrictionAsync(new SetAccountRestrictionRequest
        {
            UserId = request.UserId,
            Action = request.Action,
            Reason = request.Reason,
        }, cancellationToken: cancellationToken);

        if (request.Action is AccountRestrictionAction.Block or AccountRestrictionAction.ScheduleDeletion)
            await RevokeAllSessions(request.UserId, cancellationToken);
        else
            await refreshTokensStorage.DeleteRefreshTokensByUserIdSafe(request.UserId);

        var pendingChallenges = await identityContext.AuthenticationChallenges
            .Where(challenge => challenge.UserId == request.UserId
                && (challenge.State == AuthChallengeState.Waiting || challenge.State == AuthChallengeState.Approved))
            .ToListAsync(cancellationToken);
        foreach (var challenge in pendingChallenges)
        {
            challenge.State = AuthChallengeState.Cancelled;
            challenge.CodeHash = null;
            challenge.TelegramTokenHash = null;
        }
        await identityContext.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(new UserAccountStatusChanged
        {
            UserId = request.UserId,
            Status = (int)changed.Restriction.Status,
        }, cancellationToken);

        await SendStatusEmail(changed, request.Action, request.Reason);
        return new SetAccountRestrictionServerResponse { Restriction = changed.Restriction };
    }

    private async Task RevokeAllSessions(long userId, CancellationToken cancellationToken)
    {
        var refreshTokens = await refreshTokensStorage.GetRefreshTokens(userId);
        var devices = await usersClient.GetUserDevicesAsync(new GetUserDevicesRequest { UserId = userId },
            cancellationToken: cancellationToken);
        var deviceIds = refreshTokens.Select(token => token.DeviceId)
            .Concat(devices.Devices.Select(device => device.DeviceId))
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes + 2);
        foreach (var deviceId in deviceIds)
        {
            await publishEndpoint.Publish(new SessionRevokedEvent
            {
                UserId = userId,
                DeviceId = deviceId,
                AccessTokenExpiresAt = expiresAt,
            }, cancellationToken);
        }

        await refreshTokensStorage.DeleteRefreshTokensByUserIdSafe(userId);
        foreach (var deviceId in devices.Devices.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await usersClient.DeleteUserDeviceAsync(new DeleteUserDeviceRequest
            {
                UserId = userId,
                DeviceId = deviceId,
            }, cancellationToken: cancellationToken);
        }
    }

    private async Task SendStatusEmail(SetAccountRestrictionResponse changed,
        AccountRestrictionAction action, string reason)
    {
        if (string.IsNullOrWhiteSpace(changed.Email))
            return;

        var type = action switch
        {
            AccountRestrictionAction.Block => NotificationType.AccountBlocked,
            AccountRestrictionAction.Unblock => NotificationType.AccountUnblocked,
            AccountRestrictionAction.ScheduleDeletion => NotificationType.AccountDeletionScheduled,
            AccountRestrictionAction.CancelDeletion => NotificationType.AccountDeletionCancelled,
            _ => NotificationType.Unknown,
        };
        var deletionDate = changed.Restriction.DeletionScheduledAt?.ToDateTime()
            .ToString("dd.MM.yyyy HH:mm:ss") ?? string.Empty;
        var notification = new EmailNotification
        {
            OwnerId = changed.User.Id,
            Address = changed.Email,
            CreatedAt = DateTime.UtcNow,
            ServiceId = ServiceId.Identity,
            Type = type,
            Title = type.ToString(),
            Payload = new Dictionary<string, string>
            {
                ["username"] = changed.User.Username,
                ["reason"] = reason,
                ["datetime"] = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss"),
                ["deletiondate"] = deletionDate,
            },
        };

        try
        {
            await notificationQueueSender.SendNotification(notification);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось отправить письмо об изменении статуса аккаунта {UserId}", changed.User.Id);
        }
    }
}
