using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.LeaveChat;

public class LeaveChatCommandHandler : IRequestHandler<LeaveChatCommand, LeaveChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<LeaveChatCommandHandler> _logger;

    public LeaveChatCommandHandler(ChatsStorage chatsStorage, MessagesStorage messagesStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient, UserContext userContext,
        MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<LeaveChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<LeaveChatResponse> Handle(LeaveChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Выход пользователя {UserId} из чата {ChatId}",
            _userContext.UserId, request.ChatId
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var chat = await _chatsStorage.GetChat(request.ChatId);

        if (chat == null)
        {
            throw new NoAccessToChatException();
        }

        if (!chat.IsGroupChat)
        {
            // Личные/приватные чаты покидать нельзя — для них DeleteChat.
            throw new ChatTypeMismatchException();
        }

        await _chatsStorage.RemoveChatMember(request.ChatId, _userContext.UserId);

        var userInfoResponse = await _usersServerApiClient.GetByIdAsync(
            new GetByIdRequest { UserId = _userContext.UserId });
        var leaverName = $"{userInfoResponse.User.FirstName} {userInfoResponse.User.LastName}";

        var leaveSystemMessage = new Message
        {
            ChatId = request.ChatId,
            Content = new MessageContent
            {
                Text = $"Пользователь {leaverName} покинул групповой чат"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        leaveSystemMessage = await _messagesStorage.AddMessage(leaveSystemMessage);

        await _messageQueueSender.SendMessage(leaveSystemMessage, request.ChatId, chat.Members!.LocalUserIds());

        _metrics.Increment("chats_left");

        _logger.LogInformation(
            "Пользователь {UserId} покинул чат {ChatId}",
            _userContext.UserId, request.ChatId
        );

        return new LeaveChatResponse();
    }
}
