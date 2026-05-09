using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UnpinMessage;

public class UnpinMessageCommandHandler : IRequestHandler<UnpinMessageCommand, UnpinMessageResponse>
{
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<UnpinMessageCommandHandler> _logger;

    public UnpinMessageCommandHandler(PinnedMessagesStorage pinnedMessagesStorage, MessagesStorage messagesStorage,
        ChatsStorage chatsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext, MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<UnpinMessageCommandHandler> logger)
    {
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UnpinMessageResponse> Handle(UnpinMessageCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Открепление сообщения {MessageId} в чате {ChatId} пользователем {UserId}",
            request.MessageId,
            request.ChatId,
            _userContext.UserId
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var pin = await _pinnedMessagesStorage.GetPinByMessageIdAsync(request.ChatId, request.MessageId);

        if (pin is null)
        {
            _logger.LogInformation(
                "Сообщение {MessageId} не закреплено в чате {ChatId} — idempotent no-op",
                request.MessageId,
                request.ChatId
            );
            _metrics.Increment("messages_unpin_noop");
            return new UnpinMessageResponse();
        }

        _pinnedMessagesStorage.Remove(pin);
        await _pinnedMessagesStorage.SaveChangesAsync();

        var members = await _chatsStorage.GetChatMembers(request.ChatId, 0, int.MaxValue);
        var memberIds = members.Select(x => x.UserId).ToList();

        var unpinnerName = await GetUserDisplayNameAsync(_userContext.UserId);

        var systemMessage = new Domain.Message
        {
            ChatId = request.ChatId,
            Content = new MessageContent
            {
                Text = $"Пользователь {unpinnerName} открепил сообщение"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        systemMessage = await _messagesStorage.AddMessage(systemMessage);

        await _messageQueueSender.SendMessage(systemMessage, request.ChatId, memberIds);
        await _messageQueueSender.SendUnpinned(request.ChatId, request.MessageId, memberIds);

        _metrics.Increment("messages_unpinned");

        return new UnpinMessageResponse();
    }

    private async Task<string> GetUserDisplayNameAsync(long userId)
    {
        try
        {
            var response = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = userId });
            return $"{response.User.FirstName} {response.User.LastName}".Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить имя пользователя {UserId}", userId);
            return $"Пользователь {userId}";
        }
    }
}
