namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateMessageDeletes;

using MassTransit;

using MediatR;

public class EncryptedMessageDeletedConsumer : IConsumer<EncryptedMessageDeletedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<EncryptedMessageDeletedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public EncryptedMessageDeletedConsumer(
        IMediator mediator,
        ILogger<EncryptedMessageDeletedConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<EncryptedMessageDeletedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("encrypted_message_deleted_events_consumed");

        await _mediator.Publish(new EncryptedMessageDeletedNotification(
            context.Message.ChatId,
            context.Message.MessageId,
            context.Message.ChatMembers));
    }
}
