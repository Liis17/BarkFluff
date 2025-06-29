namespace BarkFluff.Messages.Consumers;

using MassTransit;
using Persistence.Services;
using Shared.Queue.Users;

public class UserChangedNameConsumer : IConsumer<UserChangedName>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;

    public UserChangedNameConsumer(ChatsStorage chatsStorage, ChatCache chatCache)
    {
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
    }

    public async Task Consume(ConsumeContext<UserChangedName> context)
    {
        var chatsWithUser = await _chatsStorage.GetDmChatsWithUser(context.Message.UserId);
        
        foreach (var chat in chatsWithUser)
        {
            var personDm = chat.Members![0].UserId == context.Message.UserId ? chat.Members[1].UserId : chat.Members[0].UserId;
            
            await _chatCache.SetChatName(chat.Id, personDm, $"{context.Message.NewFirstName} {context.Message.NewLastName}");
        }

    }
}