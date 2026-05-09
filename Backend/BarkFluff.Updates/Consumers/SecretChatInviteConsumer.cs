namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeSecretChatInvites;

using MassTransit;

using MediatR;

public class SecretChatInviteConsumer : IConsumer<SecretChatInviteEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<SecretChatInviteConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public SecretChatInviteConsumer(
        IMediator mediator,
        ILogger<SecretChatInviteConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<SecretChatInviteEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("secret_chat_invite_events_consumed");

        var msg = context.Message;

        await _mediator.Publish(new SecretChatInviteNotification(
            msg.InviteId,
            msg.SenderUserId,
            msg.SenderDeviceId,
            msg.RecipientUserId,
            msg.RecipientDeviceId,
            msg.InitialEnvelope,
            msg.SentAt));
    }
}
