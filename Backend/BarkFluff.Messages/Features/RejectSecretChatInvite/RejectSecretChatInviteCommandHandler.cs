using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.RejectSecretChatInvite;

public class RejectSecretChatInviteCommandHandler : IRequestHandler<RejectSecretChatInviteCommand, RejectSecretChatInviteResponse>
{
    private readonly SecretMessageBuffer _buffer;
    private readonly SecretMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<RejectSecretChatInviteCommandHandler> _logger;

    public RejectSecretChatInviteCommandHandler(
        SecretMessageBuffer buffer,
        SecretMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<RejectSecretChatInviteCommandHandler> logger)
    {
        _buffer = buffer;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<RejectSecretChatInviteResponse> Handle(RejectSecretChatInviteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new DeviceIdRequiredException();
        }

        if (string.IsNullOrEmpty(request.InviteId))
        {
            throw new SecretInviteNotFoundException();
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
            accepted: false,
            responseEnvelope: Array.Empty<byte>());

        _metrics.Increment("secret_chat_invites_rejected");

        _logger.LogInformation(
            "Инвайт {InviteId} отклонён устройством {DeviceId} пользователя {UserId}",
            request.InviteId, deviceId, _userContext.UserId);

        return new RejectSecretChatInviteResponse();
    }
}
