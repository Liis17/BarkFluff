namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;
using Features.SubscribeNewMessages;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Proto.Shared;
using Shared.Queue.Messages;

public class NewMessageConsumer : IConsumer<NewMessageEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<NewMessageConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public NewMessageConsumer(IMediator mediator, ILogger<NewMessageConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<NewMessageEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("events_broadcast");
        _logger.LogInformation(
            "Получено событие нового сообщения для чата {ChatId} с {MemberCount} участниками",
            context.Message.ChatId,
            context.Message.ChatMembers.Count
        );

        try
        {
            var message = Message.Parser.ParseFrom(context.Message.Message);

            _logger.LogDebug(
                "Парсинг сообщения {MessageId} для чата {ChatId} успешен",
                message.Id,
                context.Message.ChatId
            );

            // Публикуем уведомление через MediatR
            await _mediator.Publish(new NewMessageNotification(message, context.Message.ChatMembers, context.Message.ChatId));

            _logger.LogInformation(
                "Уведомление о новом сообщении {MessageId} опубликовано через MediatR",
                message.Id
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при обработке события нового сообщения для чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}