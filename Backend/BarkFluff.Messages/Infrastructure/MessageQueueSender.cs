namespace BarkFluff.Messages.Infrastructure;

using Domain;
using Google.Protobuf;
using Mapping;
using MassTransit;
using Shared.Queue.Messages;

public class MessageQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MessageQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task SendMessage(Message message, Guid chatId, List<long> chatMembers)
    {
        var newMessageEvent = new NewMessageEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc().ToByteArray()
        };
        
        await _publishEndpoint.Publish(newMessageEvent);
    }

    public async Task SendReadReceipt(
        Guid chatId,
        long messageId,
        List<long> readBy,
        List<long> chatMembers,
        bool isLastMessage)
    {
        var readReceiptEvent = new ReadReceiptEvent
        {
            ChatId = chatId,
            MessageId = messageId,
            ReadBy = readBy,
            ChatMembers = chatMembers,
            IsLastMessage = isLastMessage,
            ReadAt = DateTime.UtcNow
        };
        
        await _publishEndpoint.Publish(readReceiptEvent);
    }
}