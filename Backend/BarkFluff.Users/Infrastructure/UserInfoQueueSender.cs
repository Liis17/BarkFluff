using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Users;

using MassTransit;

namespace BarkFluff.Users.Infrastructure;

public class UserInfoQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly MetricsCollector _metrics;

    public UserInfoQueueSender(IPublishEndpoint publishEndpoint, MetricsCollector metrics)
    {
        _publishEndpoint = publishEndpoint;
        _metrics = metrics;
    }


    public async Task NameChangedEvent(long userId, string newFirstName, string newLastName)
    {

        var userChangeNameEvent = new UserChangedName()
        {
            UserId = userId,
            NewFirstName = newFirstName,
            NewLastName = newLastName
        };

        await _publishEndpoint.Publish(userChangeNameEvent);
        _metrics.Increment("user_events_published");
        _metrics.Increment("user_name_changed_published");
    }

    public async Task UsernameChangedEvent(long userId, string newUsername)
    {
        var usernameChangedEvent = new UserChangedUsername()
        {
            NewUsername = newUsername,
            UserId = userId
        };

        await _publishEndpoint.Publish(usernameChangedEvent);
        _metrics.Increment("user_events_published");
        _metrics.Increment("user_username_changed_published");
    }

    public async Task UserChangedAvatarEvent(long userId, string profilePictureUrl, string profilePicturePreviewUrl)
    {
        var userChangedAvatarEvent = new UserChangedAvatar()
        {
            UserId = userId,
            ProfilePictureUrl = profilePictureUrl,
            ProfilePictureUrlPreview = profilePicturePreviewUrl
        };

        await _publishEndpoint.Publish(userChangedAvatarEvent);
        _metrics.Increment("user_events_published");
        _metrics.Increment("user_avatar_changed_published");
    }

    public async Task UserChangedPasswordEvent(long userId)
    {
        var userChangedPasswordEvent = new UserChangedPassword()
        {
            UserId = userId
        };

        await _publishEndpoint.Publish(userChangedPasswordEvent);
        _metrics.Increment("user_events_published");
        _metrics.Increment("user_password_changed_published");
    }

    public async Task UserBioChangedEvent(long userId, string newUsername)
    {
        var usernameChangedEvent = new UserChangedBio()
        {
            NewBio = newUsername,
            UserId = userId
        };

        await _publishEndpoint.Publish(usernameChangedEvent);
        _metrics.Increment("user_events_published");
        _metrics.Increment("user_bio_changed_published");
    }
}