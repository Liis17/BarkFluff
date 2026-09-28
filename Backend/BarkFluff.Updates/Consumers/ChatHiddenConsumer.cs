namespace BarkFluff.Updates.Consumers;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeChatHidden;

using MassTransit;

using MediatR;

using Microsoft.Extensions.Logging;

using Shared.Queue.Messages;

public class ChatHiddenConsumer : IConsumer<ChatHiddenEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ChatHiddenConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public ChatHiddenConsumer(IMediator mediator, ILogger<ChatHiddenConsumer> logger, MetricsCollector metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<ChatHiddenEvent> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        _metrics.Increment("chats_hidden_events_consumed");
        _logger.LogInformation(
            "Получено событие скрытия чата {ChatId} с {MemberCount} участниками (ContentWiped={ContentWiped})",
            context.Message.ChatId,
            context.Message.ChatMembers.Count,
            context.Message.ContentWiped
        );

        try
        {
            await _mediator.Publish(new ChatHiddenNotification(
                context.Message.ChatId,
                context.Message.ChatMembers,
                context.Message.ContentWiped));

            _logger.LogInformation(
                "Уведомление о скрытии чата {ChatId} опубликовано через MediatR",
                context.Message.ChatId
            );
        }
        catch (Exception ex)
        {
            _metrics.Increment("chats_hidden_events_errors");
            _logger.LogError(
                ex,
                "Ошибка при обработке события скрытия чата {ChatId}",
                context.Message.ChatId
            );
            throw;
        }
    }
}
