namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateChatInvites;

using MassTransit;

using MediatR;

public class PrivateChatInviteConsumer : IConsumer<PrivateChatInviteEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<PrivateChatInviteConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public PrivateChatInviteConsumer(
        IMediator mediator,
        ILogger<PrivateChatInviteConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<PrivateChatInviteEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("private_chat_invite_events_consumed");

        var msg = context.Message;

        await _mediator.Publish(new PrivateChatInviteNotification(
            msg.ChatId,
            msg.InviterUserId,
            msg.InviteeUserId,
            msg.KdfSalt,
            msg.PassphraseVerifier,
            msg.InvitedAt));
    }
}
