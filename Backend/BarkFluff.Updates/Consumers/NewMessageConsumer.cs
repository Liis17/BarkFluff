namespace BarkFluff.Updates.Consumers;

using Features.SubscribeNewMessages;
using MassTransit;
using MediatR;
using Proto.Shared;
using Shared.Queue.Messages;

public class NewMessageConsumer : IConsumer<NewMessageEvent>
{
    private readonly IMediator _mediator;

    public NewMessageConsumer(IMediator mediator)
    {
        _mediator = mediator;
    }
    
    public async Task Consume(ConsumeContext<NewMessageEvent> context)
    {
        var message = Message.Parser.ParseFrom(context.Message.Message);
        
        // Публикуем уведомление через MediatR
        await _mediator.Publish(new NewMessageNotification(message, context.Message.ChatMembers, context.Message.ChatId));
    }
}