using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendPrivateMessage;

public class SendPrivateMessageCommandHandler : IRequestHandler<SendPrivateMessageCommand, SendPrivateMessageResponse>
{
    private const int MinNonceLength = 12;
    private const int MaxNonceLength = 32;
    private const int MaxCiphertextLength = 64 * 1024;     // 64 KiB
    private const int MaxAssociatedDataLength = 4 * 1024;  // 4 KiB

    private readonly ChatsStorage _chatsStorage;
    private readonly EncryptedMessagesStorage _storage;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SendPrivateMessageCommandHandler> _logger;

    public SendPrivateMessageCommandHandler(
        ChatsStorage chatsStorage,
        EncryptedMessagesStorage storage,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<SendPrivateMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _storage = storage;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SendPrivateMessageResponse> Handle(SendPrivateMessageCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new DeviceIdRequiredException();
        }

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

        var chat = await _chatsStorage.GetChat(request.ChatId);
        if (chat is null)
        {
            throw new ChatNotFoundException();
        }

        if (chat.Type != ChatType.Private)
        {
            throw new ChatNotPrivateException();
        }

        if (chat.Members?.All(m => m.UserId != _userContext.UserId) ?? true)
        {
            throw new NoAccessToChatException();
        }

        var saved = await _storage.AddAsync(
            request.ChatId,
            _userContext.UserId,
            deviceId,
            request.Ciphertext,
            request.Nonce,
            request.AssociatedData);

        var memberIds = chat.Members!.Select(m => m.UserId).ToList();
        await _queueSender.SendNew(saved, memberIds);

        _metrics.Increment("private_messages_sent");
        _metrics.Add("private_messages_ciphertext_bytes", request.Ciphertext.Length);

        _logger.LogInformation(
            "Зашифрованное сообщение {MessageId} в чат {ChatId} от {UserId}/{DeviceId}",
            saved.Id, request.ChatId, _userContext.UserId, deviceId);

        return new SendPrivateMessageResponse { Message = saved.ToGrpc() };
    }
}
