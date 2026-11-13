namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeChatFoldersChanged;

using MassTransit;

using MediatR;

using Shared.Queue.Users;

public class ChatFoldersChangedConsumer : IConsumer<ChatFoldersChanged>
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public ChatFoldersChangedConsumer(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<ChatFoldersChanged> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("chat_folders_changed_events_consumed");
        await _mediator.Publish(new ChatFoldersChangedNotification(context.Message.UserId), context.CancellationToken);
    }
}
