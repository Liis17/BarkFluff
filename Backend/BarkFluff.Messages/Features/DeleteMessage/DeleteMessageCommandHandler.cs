using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteMessage;

public class DeleteMessageCommandHandler : IRequestHandler<DeleteMessageCommand, DeleteMessageResponse>
{
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<DeleteMessageCommandHandler> _logger;

    public DeleteMessageCommandHandler(MessagesStorage messagesStorage, ChatsStorage chatsStorage,
        PinnedMessagesStorage pinnedMessagesStorage, UserContext userContext,
        MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<DeleteMessageCommandHandler> logger)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<DeleteMessageResponse> Handle(DeleteMessageCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Удаление сообщения {MessageId} пользователем {UserId}",
            request.MessageId,
            _userContext.UserId
        );

        var message = await _messagesStorage.GetMessageById(request.MessageId);

        if (message is null)
        {
            _logger.LogWarning(
                "Сообщение {MessageId} не найдено для удаления пользователем {UserId}",
                request.MessageId,
                _userContext.UserId
            );
            throw new MessageNotFoundException();
        }

        if (message.SenderId != _userContext.UserId)
        {
            _logger.LogWarning(
                "Пользователь {UserId} попытался удалить чужое сообщение {MessageId} (автор {SenderId})",
                _userContext.UserId,
                request.MessageId,
                message.SenderId
            );
            throw new NoPermissionException();
        }

        if (message.Type == Domain.MessageContentType.System)
        {
            _logger.LogWarning(
                "Пользователь {UserId} попытался удалить системное сообщение {MessageId}",
                _userContext.UserId,
                request.MessageId
            );
            throw new NoPermissionException();
        }

        if (message.IsDeleted)
        {
            _logger.LogWarning(
                "Сообщение {MessageId} уже удалено — повторное удаление игнорируется",
                request.MessageId
            );
            _metrics.Increment("messages_delete_noop");
            return new DeleteMessageResponse();
        }

        message.IsDeleted = true;
        message.LastChangeAt = DateTime.UtcNow;

        await _messagesStorage.SaveChangesAsync();

        var removedPin = await _pinnedMessagesStorage.RemoveByMessageIdAsync(message.Id);
        if (removedPin is not null)
        {
            await _pinnedMessagesStorage.SaveChangesAsync();
        }

        var members = await _chatsStorage.GetChatMembers(message.ChatId, 0, int.MaxValue);
        var memberIds = members.LocalUserIds();

        await _messageQueueSender.SendDeleted(message.ChatId, message.Id, memberIds);

        if (removedPin is not null)
        {
            await _messageQueueSender.SendUnpinned(message.ChatId, message.Id, memberIds);
        }

        _metrics.Increment("messages_deleted");

        _logger.LogInformation(
            "Сообщение {MessageId} удалено пользователем {UserId} в чате {ChatId}",
            request.MessageId,
            _userContext.UserId,
            message.ChatId
        );

        return new DeleteMessageResponse();
    }
}
