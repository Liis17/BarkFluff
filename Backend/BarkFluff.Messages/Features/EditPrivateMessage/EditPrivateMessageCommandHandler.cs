using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.EditPrivateMessage;

public class EditPrivateMessageCommandHandler : IRequestHandler<EditPrivateMessageCommand, EditPrivateMessageResponse>
{
    private const int MinNonceLength = 12;
    private const int MaxNonceLength = 32;
    private const int MaxCiphertextLength = 64 * 1024;
    private const int MaxAssociatedDataLength = 4 * 1024;

    private readonly ChatsStorage _chatsStorage;
    private readonly EncryptedMessagesStorage _storage;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<EditPrivateMessageCommandHandler> _logger;

    public EditPrivateMessageCommandHandler(
        ChatsStorage chatsStorage,
        EncryptedMessagesStorage storage,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<EditPrivateMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _storage = storage;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<EditPrivateMessageResponse> Handle(EditPrivateMessageCommand request, CancellationToken cancellationToken)
    {
        if (request.Ciphertext.Length is 0 or > MaxCiphertextLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        if (request.Nonce.Length is < MinNonceLength or > MaxNonceLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        if (request.AssociatedData.Length > MaxAssociatedDataLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        var existing = await _storage.GetByIdAsync(request.MessageId);
        if (existing is null || existing.IsDeleted)
        {
            throw new EncryptedMessageNotFoundException();
        }

        if (existing.SenderId != _userContext.UserId)
        {
            throw new NoPermissionException();
        }

        var chat = await _chatsStorage.GetChat(existing.ChatId);
        if (chat is null)
        {
            throw new ChatNotFoundException();
        }

        var updated = await _storage.EditAsync(
            request.MessageId,
            request.Ciphertext,
            request.Nonce,
            request.AssociatedData);

        var memberIds = chat.Members?.Select(m => m.UserId).ToList() ?? new List<long>();
        await _queueSender.SendEdited(updated, memberIds);

        _metrics.Increment("private_messages_edited");

        _logger.LogInformation(
            "Зашифрованное сообщение {MessageId} отредактировано пользователем {UserId}",
            request.MessageId, _userContext.UserId);

        return new EditPrivateMessageResponse { Message = updated.ToGrpc() };
    }
}
