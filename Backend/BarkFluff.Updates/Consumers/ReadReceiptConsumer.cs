namespace BarkFluff.Updates.Consumers;

using System.Threading.Tasks;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeReadReceipts;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

public class ReadReceiptConsumer : IConsumer<ReadReceiptEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReadReceiptConsumer> _logger;

    public ReadReceiptConsumer(IMediator mediator, ILogger<ReadReceiptConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReadReceiptEvent> context)
    {
        var message = context.Message;

        _logger.LogDebug(
            "Received read receipt event for message {MessageId} in chat {ChatId}",
            message.MessageId, message.ChatId);

        var notification = new ReadReceiptNotification(
            message.ChatId.ToString(),
            message.MessageId,
            message.ReadBy,
            message.ChatMembers,
            message.IsLastMessage,
            message.ReadAt);

        await _mediator.Publish(notification);
    }
}
