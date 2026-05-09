namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateMessageEdits;

using MassTransit;

using MediatR;

public class EncryptedMessageEditedConsumer : IConsumer<EncryptedMessageEditedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<EncryptedMessageEditedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public EncryptedMessageEditedConsumer(
        IMediator mediator,
        ILogger<EncryptedMessageEditedConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<EncryptedMessageEditedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("encrypted_message_edited_events_consumed");

        try
        {
            var message = EncryptedMessage.Parser.ParseFrom(context.Message.Message);

            await _mediator.Publish(new EncryptedMessageEditedNotification(
                message,
                context.Message.ChatMembers,
                context.Message.ChatId));
        }
        catch (Exception ex)
        {
            _metrics.Increment("encrypted_message_edited_events_errors");
            _logger.LogError(ex,
                "Ошибка обработки edit-события шифрованного сообщения для чата {ChatId}",
                context.Message.ChatId);
            throw;
        }
    }
}
