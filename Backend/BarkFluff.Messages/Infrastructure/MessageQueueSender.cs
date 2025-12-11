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
}