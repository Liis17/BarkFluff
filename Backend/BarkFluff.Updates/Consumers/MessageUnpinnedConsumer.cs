namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeMessagesUnpinned;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Queue.Messages;

public class MessageUnpinnedConsumer : IConsumer<MessageUnpinnedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<MessageUnpinnedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public MessageUnpinnedConsumer(IMediator mediator, ILogger<MessageUnpinnedConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageUnpinnedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("messages_unpinned_events_consumed");
        _logger.LogInformation(
            "Получено событие открепления сообщения {MessageId} для чата {ChatId} с {MemberCount} участниками",
            context.Message.MessageId,
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            await _mediator.Publish(new MessageUnpinnedNotification(
                context.Message.ChatId,
                context.Message.MessageId,
                context.Message.ChatMembers));

            _logger.LogInformation(
                "Уведомление об откреплении сообщения {MessageId} опубликовано через MediatR",
                context.Message.MessageId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("messages_unpinned_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события открепления сообщения для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
