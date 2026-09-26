using BarkFluff.Identity.Domain;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService
{
    private async Task<(bool EmailAvailable, bool TelegramAvailable)> LoginNotificationAvailability(
        long userId, AuthUserProperty? settings, CancellationToken ct)
    {
        var contact = await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = userId }, cancellationToken: ct);
        return (
            EmailAvailable && !string.IsNullOrWhiteSpace(contact.Contact?.Email),
            telegram.Configured && settings?.TelegramEnabled == true && settings.TelegramId.HasValue);
    }

    public async Task<LoginNotificationSettingsResponse> GetLoginNotificationSettings(CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        var settings = await EnsureSettings(userId, ct);
        var channels = await LoginNotificationAvailability(userId, settings, ct);
        var channel = settings.NotificationChannel;
        if (channels.EmailAvailable && channels.TelegramAvailable &&
            channel is not (LoginNotificationChannel.Email or LoginNotificationChannel.Telegram))
            channel = LoginNotificationChannel.Email;
        else if (channels.EmailAvailable && !channels.TelegramAvailable)
            channel = LoginNotificationChannel.Email;
        else if (!channels.EmailAvailable && channels.TelegramAvailable)
            channel = LoginNotificationChannel.Telegram;

        if (channels.EmailAvailable || channels.TelegramAvailable)
        {
            if (settings.NotificationChannel != channel)
            {
                settings.NotificationChannel = channel;
                await db.SaveChangesAsync(ct);
            }
        }

        return new LoginNotificationSettingsResponse
        {
            Channel = channel,
            EmailAvailable = channels.EmailAvailable,
            TelegramAvailable = channels.TelegramAvailable
        };
    }

    public async Task<LoginNotificationSettingsResponse> SetLoginNotificationChannel(
        SetLoginNotificationChannelRequest input, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        await GuardRequest($"user:{userId}", false, ct);
        return await store.ForUser(userId, async () =>
        {
            var settings = await EnsureSettings(userId, ct);
            var channels = await LoginNotificationAvailability(userId, settings, ct);
            var available = input.Channel switch
            {
                LoginNotificationChannel.Email => channels.EmailAvailable,
                LoginNotificationChannel.Telegram => channels.TelegramAvailable,
                _ => false
            };
            if (!available) throw Invalid("Choose an available login notification channel");

            settings.NotificationChannel = input.Channel;
            await db.SaveChangesAsync(ct);
            return new LoginNotificationSettingsResponse
            {
                Channel = settings.NotificationChannel,
                EmailAvailable = channels.EmailAvailable,
                TelegramAvailable = channels.TelegramAvailable
            };
        }, ct);
    }
}
