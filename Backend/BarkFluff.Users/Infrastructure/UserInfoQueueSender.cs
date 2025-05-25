using BarkFluff.Shared.Queue.Users;
using MassTransit;

namespace BarkFluff.Users.Infrastructure;

public class UserInfoQueueSender
{
    private readonly IBus _rabbitMqBus;

    public UserInfoQueueSender(IBus rabbitMqBus)
    {
        _rabbitMqBus = rabbitMqBus;
    }

    public async Task NameChangedEvent(long userId, string newFirstName, string newLastName)
    {
        var endpoint = await _rabbitMqBus.GetSendEndpoint(new Uri(Endpoints.UserChangeName));

        var userChangeNameEvent = new UserChangedName()
        {
            UserId = userId,
            NewFirstName = newFirstName,
            NewLastName = newLastName
        };
        
        await endpoint.Send(userChangeNameEvent);
    }

    public async Task UsernameChangedEvent(long userId, string newUsername)
    {
        var endpoint = await _rabbitMqBus.GetSendEndpoint(new Uri(Endpoints.UserChangeUsername));

        var usernameChangedEvent = new UserChangedUsername()
        {
            NewUsername = newUsername,
            UserId = userId
        };
        
        await endpoint.Send(usernameChangedEvent);
    }

    public async Task UserChangedAvatarEvent(long userId, string profilePictureUrl)
    {
        var endpoint = await _rabbitMqBus.GetSendEndpoint(new Uri(Endpoints.UserChangeAvatar));

        var userChangedAvatarEvent = new UserChangedAvatar()
        {
            UserId = userId,
            ProfilePictureUrl = profilePictureUrl
        };
        
        await endpoint.Send(userChangedAvatarEvent);
    }

    public async Task UserChangedPasswordEvent(long userId)
    {
        var endpoint = await _rabbitMqBus.GetSendEndpoint(new Uri(Endpoints.UserChangePassword));

        var userChangedPasswordEvent = new UserChangedPassword()
        {
            UserId = userId
        };
        
        await endpoint.Send(userChangedPasswordEvent);
    }
}