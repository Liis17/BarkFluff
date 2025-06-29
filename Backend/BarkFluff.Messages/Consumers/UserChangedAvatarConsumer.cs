namespace BarkFluff.Messages.Consumers;

using MassTransit;
using Persistence.Services;
using Shared.Queue.Users;

public class UserChangedAvatarConsumer : IConsumer<UserChangedAvatar>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;

    public UserChangedAvatarConsumer(ChatsStorage chatsStorage, ChatCache chatCache)
    {
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
    }

    public async Task Consume(ConsumeContext<UserChangedAvatar> context)
    {
        var chatsWithUser = await _chatsStorage.GetDmChatsWithUser(context.Message.UserId);
        
        foreach (var chat in chatsWithUser)
        {
            var personDm = chat.Members![0].UserId == context.Message.UserId ? chat.Members[1].UserId : chat.Members[0].UserId;
            
            await _chatCache.SetChatImage(chat.Id, personDm, context.Message.ProfilePictureUrl);
        }
        
    }
}