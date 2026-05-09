namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeAllMessagesUnpinned;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Queue.Messages;

public class AllMessagesUnpinnedConsumer : IConsumer<AllMessagesUnpinnedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<AllMessagesUnpinnedConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public AllMessagesUnpinnedConsumer(IMediator mediator, ILogger<AllMessagesUnpinnedConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<AllMessagesUnpinnedEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("all_messages_unpinned_events_consumed");
        _logger.LogInformation(
            "Получено событие открепления всех сообщений в чате {ChatId} с {MemberCount} участниками",
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            await _mediator.Publish(new AllMessagesUnpinnedNotification(
                context.Message.ChatId,
                context.Message.ChatMembers));

            _logger.LogInformation(
                "Уведомление об откреплении всех сообщений в чате {ChatId} опубликовано через MediatR",
                context.Message.ChatId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("all_messages_unpinned_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события открепления всех сообщений для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
