using BarkFluff.Shared.Queue.Users;
using MassTransit;

namespace BarkFluff.Users.Infrastructure;

public class UserInfoQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public UserInfoQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
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
    }

    public async Task UsernameChangedEvent(long userId, string newUsername)
    {
        var usernameChangedEvent = new UserChangedUsername()
        {
            NewUsername = newUsername,
            UserId = userId
        };
        
        await _publishEndpoint.Publish(usernameChangedEvent);
    }

    public async Task UserChangedAvatarEvent(long userId, string profilePictureUrl)
    {
        var userChangedAvatarEvent = new UserChangedAvatar()
        {
            UserId = userId,
            ProfilePictureUrl = profilePictureUrl
        };
        
        await _publishEndpoint.Publish(userChangedAvatarEvent);
    }

    public async Task UserChangedPasswordEvent(long userId)
    {
        var userChangedPasswordEvent = new UserChangedPassword()
        {
            UserId = userId
        };
        
        await _publishEndpoint.Publish(userChangedPasswordEvent);
    }
    
    public async Task UserBioChangedEvent(long userId, string newUsername)
    {
        var usernameChangedEvent = new UserChangedBio()
        {
            NewBio = newUsername,
            UserId = userId
        };
        
        await _publishEndpoint.Publish(usernameChangedEvent);
    }
}