using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UnpinAll;

public class UnpinAllCommandHandler : IRequestHandler<UnpinAllCommand, UnpinAllResponse>
{
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<UnpinAllCommandHandler> _logger;

    public UnpinAllCommandHandler(PinnedMessagesStorage pinnedMessagesStorage, MessagesStorage messagesStorage,
        ChatsStorage chatsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext, MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<UnpinAllCommandHandler> logger)
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

    public async Task<UnpinAllResponse> Handle(UnpinAllCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Открепление всех сообщений в чате {ChatId} пользователем {UserId}",
            request.ChatId,
            _userContext.UserId
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var totalPinned = await _pinnedMessagesStorage.CountByChatAsync(request.ChatId);

        if (totalPinned == 0)
        {
            _logger.LogInformation(
                "В чате {ChatId} нет закреплённых сообщений — idempotent no-op",
                request.ChatId
            );
            return new UnpinAllResponse { UnpinnedCount = 0 };
        }

        var removedCount = await _pinnedMessagesStorage.RemoveAllByChatAsync(request.ChatId);
        await _pinnedMessagesStorage.SaveChangesAsync();

        var members = await _chatsStorage.GetChatMembers(request.ChatId, 0, int.MaxValue);
        var memberIds = members.LocalUserIds();

        var unpinnerName = await GetUserDisplayNameAsync(_userContext.UserId);

        var systemMessage = new Domain.Message
        {
            ChatId = request.ChatId,
            Content = new MessageContent
            {
                Text = $"Пользователь {unpinnerName} открепил все сообщения"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        systemMessage = await _messagesStorage.AddMessage(systemMessage);

        await _messageQueueSender.SendMessage(systemMessage, request.ChatId, memberIds);
        await _messageQueueSender.SendAllUnpinned(request.ChatId, memberIds);

        _metrics.Increment("messages_unpinned_all");

        return new UnpinAllResponse { UnpinnedCount = removedCount };
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
