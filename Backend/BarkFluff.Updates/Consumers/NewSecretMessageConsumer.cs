namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeSecretMessages;

using MassTransit;

using MediatR;

public class NewSecretMessageConsumer : IConsumer<NewSecretMessageEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<NewSecretMessageConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public NewSecretMessageConsumer(
        IMediator mediator,
        ILogger<NewSecretMessageConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<NewSecretMessageEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("new_secret_message_events_consumed");

        var msg = context.Message;

        await _mediator.Publish(new NewSecretMessageNotification(
            msg.MessageId,
            msg.SenderUserId,
            msg.SenderDeviceId,
            msg.RecipientUserId,
            msg.RecipientDeviceId,
            msg.Envelope,
            msg.SentAt));
    }
}
