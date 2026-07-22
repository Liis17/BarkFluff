using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AddUser;

using Infrastructure;

public class AddUserCommandHandler : IRequestHandler<AddUserCommand>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly MessagesStorage _messagesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AddUserCommandHandler> _logger;

    public AddUserCommandHandler(ChatsStorage chatsStorage, UserContext userContext, MessagesStorage messagesStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient, MessageQueueSender messageQueueSender,
        MetricsCollector metrics, ILogger<AddUserCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _messagesStorage = messagesStorage;
        _usersServerApiClient = usersServerApiClient;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(AddUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Добавление пользователя {AddedUserId} в чат {ChatId} администратором {AdminId}",
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

        if (chatInfo.Members!.Any(x => x.UserId == request.UserId))
        {
            throw new UserAlreadyMemberChatException();
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
                "Пользователь {UserId} не имеет прав на добавление участников в чат {ChatId}",
                _userContext.UserId,
                request.ChatId
            );
            throw new NoPermissionException();
        }

        _logger.LogDebug("Добавление пользователя {UserId} в чат {ChatId}", request.UserId, request.ChatId);

        await _chatsStorage.AddChatMember(request.ChatId, request.UserId);

        var administatorUserInfoResponse = await
            _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = _userContext.UserId });

        var addedUserInfoResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = request.UserId });

        var adminName = $"{administatorUserInfoResponse.User.FirstName} {administatorUserInfoResponse.User.LastName}";

        var addedName = $"{addedUserInfoResponse.User.FirstName} {addedUserInfoResponse.User.LastName}";

        var addSystemMessage = new Message()
        {
            ChatId = request.ChatId,
            Content = new MessageContent()
            {
                Text = $"Администратор {adminName} добавил пользователя {addedName} в групповой чат"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        addSystemMessage = await _messagesStorage.AddMessage(addSystemMessage);

        _logger.LogDebug("Отправка системного сообщения о добавлении пользователя");

        var recipients = chatInfo.Members!.LocalUserIds();
        recipients.Add(request.UserId);

        await _messageQueueSender.SendMessage(addSystemMessage, request.ChatId, recipients);

        _metrics.Increment("users_added");

        _logger.LogInformation(
            "Пользователь {AddedUser} ({AddedUserId}) успешно добавлен в чат {ChatId} администратором {AdminUser} ({AdminId})",
            addedName,
            request.UserId,
            request.ChatId,
            adminName,
            _userContext.UserId
        );
    }
}
