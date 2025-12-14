using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeMessagesRead;
using MassTransit;
using MediatR;

namespace BarkFluff.Updates.Consumers;

public class ReadByConsumer : IConsumer<MessageReadEvent>
{
    private readonly IMediator _mediator;

    public ReadByConsumer(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(ConsumeContext<MessageReadEvent> context)
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
    }
}