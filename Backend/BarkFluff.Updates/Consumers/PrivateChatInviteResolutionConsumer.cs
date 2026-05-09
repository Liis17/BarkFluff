namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateChatInviteResolutions;

using MassTransit;

using MediatR;

public class PrivateChatInviteResolutionConsumer : IConsumer<PrivateChatInviteResolutionEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<PrivateChatInviteResolutionConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public PrivateChatInviteResolutionConsumer(
        IMediator mediator,
        ILogger<PrivateChatInviteResolutionConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<PrivateChatInviteResolutionEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("private_chat_invite_resolution_events_consumed");

        var msg = context.Message;

        await _mediator.Publish(new PrivateChatInviteResolutionNotification(
            msg.ChatId,
            msg.InviterUserId,
            msg.InviteeUserId,
            msg.Accepted));
    }
}
