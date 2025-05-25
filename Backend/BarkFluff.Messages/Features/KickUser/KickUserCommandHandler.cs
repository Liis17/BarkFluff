using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.KickUser;

public class KickUserCommandHandler : IRequestHandler<KickUserCommand>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly MessagesStorage _messagesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;

    public KickUserCommandHandler(ChatsStorage chatsStorage, UserContext userContext, MessagesStorage messagesStorage, 
        UsersServerApi.UsersServerApiClient usersServerApiClient)
    {
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _messagesStorage = messagesStorage;
        _usersServerApiClient = usersServerApiClient;
    }

    public async Task Handle(KickUserCommand request, CancellationToken cancellationToken)
    {
        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var chatInfo = await _chatsStorage.GetChat(request.ChatId);

        if (chatInfo == null)
        {
            throw new NoAccessToChatException();
        }
        
        if (!chatInfo.IsGroupChat)
        {
            throw new IsNotGroupChatException();
        }
        
        var chatMember = chatInfo.Members!.FirstOrDefault(x => x.Id == request.UserId);

        if (chatMember == null)
        {
            throw new UserNotMemberChatException();
        }
        
        var groupChatInfo = await _chatsStorage.GetGroupChatInfo(request.ChatId);
        
        if (groupChatInfo == null)
        {
            throw new NoAccessToChatException();
        }

        if (!groupChatInfo.UsersCanKick.Contains(chatMember.UserId))
        {
            throw new NoPermissionException();
        }
        
        await _chatsStorage.RemoveChatMember(request.ChatId, chatMember.UserId);

        var administatorUserInfoResponse = await
            _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = _userContext.UserId });
        
        var kickedUserInfoResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = chatMember.UserId });
        
        var adminName = $"{administatorUserInfoResponse.User.FirstName} {administatorUserInfoResponse.User.LastName}";

        var kickedName = $"{kickedUserInfoResponse.User.FirstName} {kickedUserInfoResponse.User.LastName}";
        
        var kickSystemMessage = new Message()
        {
            ChatId = request.ChatId,
            Content = new MessageContent()
            {
                Text = $"Администратор {adminName} исключил пользователя {kickedName} из группового чата"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };
        
        await _messagesStorage.AddMessage(kickSystemMessage);
    }
}