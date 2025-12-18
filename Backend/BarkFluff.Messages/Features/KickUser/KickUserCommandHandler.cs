using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Messages.Features.KickUser;

using Infrastructure;

public class KickUserCommandHandler : IRequestHandler<KickUserCommand>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly MessagesStorage _messagesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly ILogger<KickUserCommandHandler> _logger;

    public KickUserCommandHandler(ChatsStorage chatsStorage, UserContext userContext, MessagesStorage messagesStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient, MessageQueueSender messageQueueSender,
        ILogger<KickUserCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _messagesStorage = messagesStorage;
        _usersServerApiClient = usersServerApiClient;
        _messageQueueSender = messageQueueSender;
        _logger = logger;
    }

    public async Task Handle(KickUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Исключение пользователя {KickedUserId} из чата {ChatId} администратором {AdminId}",
            request.UserId,
            request.ChatId,
            _userContext.UserId
        );
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
            _logger.LogWarning("Информация о групповом чате {ChatId} не найдена", request.ChatId);
            throw new NoAccessToChatException();
        }

        if (!groupChatInfo.UsersCanKick.Contains(_userContext.UserId))
        {
            _logger.LogWarning(
                "Пользователь {UserId} не имеет прав на исключение участников из чата {ChatId}",
                _userContext.UserId,
                request.ChatId
            );
            throw new NoPermissionException();
        }

        _logger.LogDebug("Удаление пользователя {UserId} из чата {ChatId}", chatMember.UserId, request.ChatId);

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
        
        _logger.LogDebug("Отправка системного сообщения об исключении пользователя");

        await _messageQueueSender.SendMessage(kickSystemMessage, request.ChatId, chatInfo.Members!
            .Select(x => x.UserId).ToList());

        await _messagesStorage.AddMessage(kickSystemMessage);

        _logger.LogInformation(
            "Пользователь {KickedUser} ({KickedUserId}) успешно исключен из чата {ChatId} администратором {AdminUser} ({AdminId})",
            kickedName,
            chatMember.UserId,
            request.ChatId,
            adminName,
            _userContext.UserId
        );
    }
}