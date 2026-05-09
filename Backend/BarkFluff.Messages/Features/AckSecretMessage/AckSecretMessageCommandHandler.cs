using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AckSecretMessage;

public class AckSecretMessageCommandHandler : IRequestHandler<AckSecretMessageCommand, AckSecretMessageResponse>
{
    private readonly SecretMessageBuffer _buffer;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AckSecretMessageCommandHandler> _logger;

    public AckSecretMessageCommandHandler(
        SecretMessageBuffer buffer,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<AckSecretMessageCommandHandler> logger)
    {
        _buffer = buffer;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AckSecretMessageResponse> Handle(AckSecretMessageCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new DeviceIdRequiredException();
        }

        if (string.IsNullOrEmpty(request.MessageId))
        {
            return new AckSecretMessageResponse();
        }

        await _buffer.AckMessageAsync(deviceId, request.MessageId);

        _metrics.Increment("secret_messages_acked");

        _logger.LogDebug(
            "Ack секретного сообщения {MessageId} устройством {DeviceId}",
            request.MessageId, deviceId);

        return new AckSecretMessageResponse();
    }
}
