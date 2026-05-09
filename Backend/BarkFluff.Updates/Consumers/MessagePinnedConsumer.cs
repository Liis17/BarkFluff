namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeMessagesPinned;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Queue.Messages;

public class MessagePinnedConsumer : IConsumer<MessagePinnedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<MessagePinnedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public MessagePinnedConsumer(IMediator mediator, ILogger<MessagePinnedConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessagePinnedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("messages_pinned_events_consumed");
        _logger.LogInformation(
            "Получено событие закрепления сообщения {MessageId} для чата {ChatId} с {MemberCount} участниками",
            context.Message.MessageId,
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            await _mediator.Publish(new MessagePinnedNotification(
                context.Message.ChatId,
                context.Message.MessageId,
                context.Message.PinnerUserId,
                context.Message.PinnedAt,
                context.Message.ChatMembers));

            _logger.LogInformation(
                "Уведомление о закреплении сообщения {MessageId} опубликовано через MediatR",
                context.Message.MessageId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("messages_pinned_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события закрепления сообщения для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
