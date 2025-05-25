using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Chat = BarkFluff.Messages.Domain.Chat;

namespace BarkFluff.Messages.Features.ListChats;

public class ListChatsCommandHandler : IRequestHandler<ListChatsCommand, ListChatsResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;

    public ListChatsCommandHandler(UserContext userContext, ChatsStorage chatsStorage, IDistributedCache cache, 
        ChatCache chatCache, UsersServerApi.UsersServerApiClient usersServerApiClient)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _usersServerApiClient = usersServerApiClient;
    }

    public async Task<ListChatsResponse> Handle(ListChatsCommand request, CancellationToken cancellationToken)
    {
        if (request.Size > 50)
        {
            request.Size = 50;
        }
        
        var chats = await _chatsStorage.GetUserChats(_userContext.UserId, request.Skip, request.Size);

        foreach (var chat in chats.Where(x => !x.IsGroupChat))
        {
            var chatName = await _chatCache.GetChatName(chat.Id, _userContext.UserId);

            if (chatName is null)
            {
                await LoadNameAndImageChat(chat);
            }
            else
            {
                var chatImage = await _chatCache.GetChatImage(chat.Id, _userContext.UserId);

                chat.Title = chatName;
                chat.Picture = chatImage;
            }
        }

        foreach (var groupChat in chats.Where(x=> x.IsGroupChat))
        {
            groupChat.Members = [];
        }
        
        var totalCount = await _chatsStorage.GetTotalUserChats(_userContext.UserId);

        return new ListChatsResponse { Chats = { chats.Select(x => x.ToGrpc()) }, TotalCount = totalCount};
    }

    private async Task LoadNameAndImageChat(Chat chat)
    {
        var memberId = chat.Members[0].Id == _userContext.UserId ? chat.Members[1].UserId : chat.Members[0].UserId;

        var userInfo = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = memberId });

        chat.Title = $"{userInfo.User.FirstName} {userInfo.User.LastName}";
        chat.Picture = userInfo.User.ProfilePicture;
        
        await _chatCache.SetChatImage(chat.Id, _userContext.UserId, chat.Picture);
        await _chatCache.SetChatName(chat.Id, _userContext.UserId, chat.Title);
    }
}