namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeSecretChatResolutions;

using MassTransit;

using MediatR;

public class SecretChatInviteResolutionConsumer : IConsumer<SecretChatInviteResolutionEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<SecretChatInviteResolutionConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public SecretChatInviteResolutionConsumer(
        IMediator mediator,
        ILogger<SecretChatInviteResolutionConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<SecretChatInviteResolutionEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("secret_chat_invite_resolution_events_consumed");

        var msg = context.Message;

        await _mediator.Publish(new SecretChatInviteResolutionNotification(
            msg.InviteId,
            msg.SenderUserId,
            msg.SenderDeviceId,
            msg.RecipientUserId,
            msg.RecipientDeviceId,
            msg.Accepted,
            msg.ResponseEnvelope));
    }
}
