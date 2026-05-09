using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Messages.Features.SendSecretChatInvite;

public class SendSecretChatInviteCommandHandler : IRequestHandler<SendSecretChatInviteCommand, SendSecretChatInviteResponse>
{
    private const int MinEnvelopeLength = 32;
    private const int MaxEnvelopeLength = 16 * 1024;

    private readonly SecretMessageBuffer _buffer;
    private readonly SecretMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SendSecretChatInviteCommandHandler> _logger;

    public SendSecretChatInviteCommandHandler(
        SecretMessageBuffer buffer,
        SecretMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<SendSecretChatInviteCommandHandler> logger)
    {
        _buffer = buffer;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SendSecretChatInviteResponse> Handle(SendSecretChatInviteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var senderDeviceId))
        {
            throw new DeviceIdRequiredException();
        }

        if (request.RecipientUserId == _userContext.UserId && request.RecipientDeviceId == senderDeviceId)
        {
            throw new SourceForSendMessageNotSetException();
        }

        if (request.InitialEnvelope.Length is < MinEnvelopeLength or > MaxEnvelopeLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        var (inviteId, expiresAt) = await _buffer.EnqueueInviteAsync(
            _userContext.UserId,
            senderDeviceId,
            request.RecipientUserId,
            request.RecipientDeviceId,
            request.InitialEnvelope);

        await _queueSender.SendInvite(
            inviteId,
            _userContext.UserId,
            senderDeviceId,
            request.RecipientUserId,
            request.RecipientDeviceId,
            request.InitialEnvelope,
            DateTime.UtcNow);

        await _queueSender.SendSilentPush(request.RecipientUserId, "Новый секретный чат");

        _metrics.Increment("secret_chat_invites_sent");

        _logger.LogInformation(
            "Инвайт {InviteId} секретного чата от {UserId}/{SenderDeviceId} к {RecipientUserId}/{RecipientDeviceId}",
            inviteId, _userContext.UserId, senderDeviceId, request.RecipientUserId, request.RecipientDeviceId);

        return new SendSecretChatInviteResponse
        {
            InviteId = inviteId,
            InviteExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc))
        };
    }
}
