using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeletePrivateMessage;

public class DeletePrivateMessageCommandHandler : IRequestHandler<DeletePrivateMessageCommand, DeletePrivateMessageResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly EncryptedMessagesStorage _storage;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<DeletePrivateMessageCommandHandler> _logger;

    public DeletePrivateMessageCommandHandler(
        ChatsStorage chatsStorage,
        EncryptedMessagesStorage storage,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<DeletePrivateMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _storage = storage;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<DeletePrivateMessageResponse> Handle(DeletePrivateMessageCommand request, CancellationToken cancellationToken)
    {
        var existing = await _storage.GetByIdAsync(request.MessageId);
        if (existing is null)
        {
            throw new EncryptedMessageNotFoundException();
        }

        if (existing.SenderId != _userContext.UserId)
        {
            throw new NoPermissionException();
        }

        if (existing.IsDeleted)
        {
            return new DeletePrivateMessageResponse();
        }

        var deleted = await _storage.SoftDeleteAsync(request.MessageId);
        if (!deleted)
        {
            return new DeletePrivateMessageResponse();
        }

        var chat = await _chatsStorage.GetChat(existing.ChatId);
        var memberIds = chat?.Members?.Select(m => m.UserId).ToList() ?? new List<long>();

        await _queueSender.SendDeleted(existing.ChatId, request.MessageId, memberIds);

        _metrics.Increment("private_messages_deleted");

        _logger.LogInformation(
            "Зашифрованное сообщение {MessageId} удалено пользователем {UserId}",
            request.MessageId, _userContext.UserId);

        return new DeletePrivateMessageResponse();
    }
}
