namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeMessagesDeleted;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Queue.Messages;

public class MessageDeletedConsumer : IConsumer<MessageDeletedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<MessageDeletedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public MessageDeletedConsumer(IMediator mediator, ILogger<MessageDeletedConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageDeletedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("messages_deleted_events_consumed");
        _logger.LogInformation(
            "Получено событие удаления сообщения {MessageId} для чата {ChatId} с {MemberCount} участниками",
            context.Message.MessageId,
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            await _mediator.Publish(new MessageDeletedNotification(
                context.Message.MessageId,
                context.Message.ChatMembers,
                context.Message.ChatId));

            _logger.LogInformation(
                "Уведомление об удалении сообщения {MessageId} опубликовано через MediatR",
                context.Message.MessageId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("messages_deleted_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события удаления сообщения для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
