using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace BarkFluff.Messages.Infrastructure;

public class ReadByQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public ReadByQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task SendEvent(
        Guid chatId,
        long messageId,
        List<long> readBy,
        List<long> newReaders,
        List<long> chatMembers)
    {
        var newEvent = new MessageReadEvent()
        {
            ChatId = chatId,
            MessageId = messageId,
            NewReadBy = readBy,
            NewReaders = newReaders,
            ChatMembers = chatMembers
        };

        await _publishEndpoint.Publish(newEvent);
    }
}
