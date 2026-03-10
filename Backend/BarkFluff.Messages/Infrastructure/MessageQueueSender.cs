namespace BarkFluff.Messages.Infrastructure;

using Domain;

using Google.Protobuf;

using Mapping;

using MassTransit;

using Proto.Files;

using Shared.Queue.Messages;

public class MessageQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MessageQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task SendMessage(Message message, Guid chatId, List<long> chatMembers, Dictionary<string, UploadFileInfo>? filesInfoMap = null)
    {
        var newMessageEvent = new NewMessageEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc(filesInfoMap).ToByteArray()
        };

        await _publishEndpoint.Publish(newMessageEvent);
    }
}