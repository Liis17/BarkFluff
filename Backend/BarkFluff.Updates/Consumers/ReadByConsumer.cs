using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeMessagesRead;

using MassTransit;

using MediatR;

namespace BarkFluff.Updates.Consumers;

public class ReadByConsumer : IConsumer<MessageReadEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReadByConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public ReadByConsumer(IMediator mediator, ILogger<ReadByConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageReadEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("read_by_events_consumed");
        _logger.LogInformation(
            "Получено событие прочтения сообщения {MessageId} в чате {ChatId}. Прочитано пользователями: {ReadByCount}",
            context.Message.MessageId,
            context.Message.ChatId,
            context.Message.NewReadBy.Count
        );

        try
        {
            var notification = new ReadByNotification()
            {
                MessageId = context.Message.MessageId,
                ChatId = context.Message.ChatId,
                ChatMembers = context.Message.ChatMembers,
                NewReadBy = context.Message.NewReadBy,
            };

            // Публикуем уведомление через MediatR
            await _mediator.Publish(notification);

            _logger.LogInformation(
                "Уведомление о прочтении сообщения {MessageId} опубликовано через MediatR",
                context.Message.MessageId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("read_by_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события прочтения сообщения {MessageId} в чате {ChatId}",
                context.Message.MessageId,
                context.Message.ChatId
            );
            throw;
        }
    }
}