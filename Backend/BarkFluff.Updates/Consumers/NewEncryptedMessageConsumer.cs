namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribePrivateMessages;

using MassTransit;

using MediatR;

public class NewEncryptedMessageConsumer : IConsumer<NewEncryptedMessageEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<NewEncryptedMessageConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public NewEncryptedMessageConsumer(
        IMediator mediator,
        ILogger<NewEncryptedMessageConsumer> logger,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<NewEncryptedMessageEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("new_encrypted_message_events_consumed");

        try
        {
            var message = EncryptedMessage.Parser.ParseFrom(context.Message.Message);

            await _mediator.Publish(new NewEncryptedMessageNotification(
                message,
                context.Message.ChatMembers,
                context.Message.ChatId));

            _logger.LogInformation(
                "Уведомление о шифрованном сообщении {MessageId} (чат {ChatId}) опубликовано",
                message.Id, context.Message.ChatId);
        }
        catch (Exception ex)
        {
            _metrics.Increment("new_encrypted_message_events_errors");
            _logger.LogError(ex,
                "Ошибка обработки события шифрованного сообщения для чата {ChatId}",
                context.Message.ChatId);
            throw;
        }
    }
}
