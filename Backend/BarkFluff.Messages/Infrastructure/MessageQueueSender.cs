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

    public async Task SendEdited(Message message, Guid chatId, List<long> chatMembers, Dictionary<string, UploadFileInfo>? filesInfoMap = null)
    {
        var editedEvent = new MessageEditedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc(filesInfoMap).ToByteArray()
        };

        await _publishEndpoint.Publish(editedEvent);
    }

    public async Task SendDeleted(Guid chatId, long messageId, List<long> chatMembers)
    {
        var deletedEvent = new MessageDeletedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId
        };

        await _publishEndpoint.Publish(deletedEvent);
    }

    public async Task SendPinned(Guid chatId, long messageId, long pinnerUserId, DateTime pinnedAt, List<long> chatMembers)
    {
        var pinnedEvent = new MessagePinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId,
            PinnerUserId = pinnerUserId,
            PinnedAt = pinnedAt
        };

        await _publishEndpoint.Publish(pinnedEvent);
    }

    public async Task SendUnpinned(Guid chatId, long messageId, List<long> chatMembers)
    {
        var unpinnedEvent = new MessageUnpinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId
        };

        await _publishEndpoint.Publish(unpinnedEvent);
    }

    public async Task SendAllUnpinned(Guid chatId, List<long> chatMembers)
    {
        var allUnpinnedEvent = new AllMessagesUnpinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers
        };

        await _publishEndpoint.Publish(allUnpinnedEvent);
    }
}
