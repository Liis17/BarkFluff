namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateMessagesRead;

using MassTransit;
using MediatR;

public class PrivateMessagesReadConsumer : IConsumer<PrivateMessagesReadEvent>
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public PrivateMessagesReadConsumer(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<PrivateMessagesReadEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("private_messages_read_events_consumed");
        await _mediator.Publish(new PrivateMessagesReadNotification(
            context.Message.ChatId,
            context.Message.UserId,
            context.Message.LastReadMessageId,
            context.Message.ChatMembers));
    }
}
