namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeMessagesEdited;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Proto.Shared;

using Shared.Queue.Messages;

public class MessageEditedConsumer : IConsumer<MessageEditedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<MessageEditedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public MessageEditedConsumer(IMediator mediator, ILogger<MessageEditedConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageEditedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("messages_edited_events_consumed");
        _logger.LogInformation(
            "Получено событие правки сообщения для чата {ChatId} с {MemberCount} участниками",
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            var message = Message.Parser.ParseFrom(context.Message.Message);

            await _mediator.Publish(new MessageEditedNotification(message, context.Message.ChatMembers, context.Message.ChatId));

            _logger.LogInformation(
                "Уведомление о правке сообщения {MessageId} опубликовано через MediatR",
                message.Id
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("messages_edited_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события правки сообщения для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
