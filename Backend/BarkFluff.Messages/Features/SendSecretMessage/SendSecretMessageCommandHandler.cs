using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Messages.Features.SendSecretMessage;

public class SendSecretMessageCommandHandler : IRequestHandler<SendSecretMessageCommand, SendSecretMessageResponse>
{
    private const int MinEnvelopeLength = 16;
    private const int MaxEnvelopeLength = 16 * 1024;

    private readonly SecretMessageBuffer _buffer;
    private readonly SecretMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SendSecretMessageCommandHandler> _logger;

    public SendSecretMessageCommandHandler(
        SecretMessageBuffer buffer,
        SecretMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<SendSecretMessageCommandHandler> logger)
    {
        _buffer = buffer;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SendSecretMessageResponse> Handle(SendSecretMessageCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var senderDeviceId))
        {
            throw new DeviceIdRequiredException();
        }

        if (request.Envelope.Length is < MinEnvelopeLength or > MaxEnvelopeLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        var (messageId, expiresAt) = await _buffer.EnqueueMessageAsync(
            _userContext.UserId,
            senderDeviceId,
            request.RecipientDeviceId,
            request.Envelope);

        await _queueSender.SendMessage(
            messageId,
            _userContext.UserId,
            senderDeviceId,
            request.RecipientUserId,
            request.RecipientDeviceId,
            request.Envelope,
            DateTime.UtcNow);

        await _queueSender.SendSilentPush(request.RecipientUserId, "Новое секретное сообщение");

        _metrics.Increment("secret_messages_sent");
        _metrics.Add("secret_messages_envelope_bytes", request.Envelope.Length);

        _logger.LogInformation(
            "Секретное сообщение {MessageId} от {UserId}/{SenderDeviceId} → {RecipientUserId}/{RecipientDeviceId}",
            messageId, _userContext.UserId, senderDeviceId, request.RecipientUserId, request.RecipientDeviceId);

        return new SendSecretMessageResponse
        {
            MessageId = messageId,
            ExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc))
        };
    }
}
