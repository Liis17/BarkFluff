using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AcceptSecretChatInvite;

public class AcceptSecretChatInviteCommandHandler : IRequestHandler<AcceptSecretChatInviteCommand, AcceptSecretChatInviteResponse>
{
    private const int MaxResponseEnvelopeLength = 16 * 1024;

    private readonly SecretMessageBuffer _buffer;
    private readonly SecretMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AcceptSecretChatInviteCommandHandler> _logger;

    public AcceptSecretChatInviteCommandHandler(
        SecretMessageBuffer buffer,
        SecretMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<AcceptSecretChatInviteCommandHandler> logger)
    {
        _buffer = buffer;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AcceptSecretChatInviteResponse> Handle(AcceptSecretChatInviteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new DeviceIdRequiredException();
        }

        if (string.IsNullOrEmpty(request.InviteId))
        {
            throw new SecretInviteNotFoundException();
        }

        if (request.ResponseEnvelope.Length > MaxResponseEnvelopeLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        var invite = await _buffer.ConsumeInviteAsync(deviceId, request.InviteId);
        if (invite is null)
        {
            throw new SecretInviteNotFoundException();
        }

        if (invite.RecipientUserId != _userContext.UserId)
        {
            throw new NoAccessToChatException();
        }

        await _queueSender.SendInviteResolution(
            request.InviteId,
            invite.SenderUserId,
            invite.SenderDeviceId,
            _userContext.UserId,
            deviceId,
            accepted: true,
            responseEnvelope: request.ResponseEnvelope);

        _metrics.Increment("secret_chat_invites_accepted");

        _logger.LogInformation(
            "Инвайт {InviteId} принят устройством {DeviceId} пользователя {UserId}",
            request.InviteId, deviceId, _userContext.UserId);

        return new AcceptSecretChatInviteResponse();
    }
}
