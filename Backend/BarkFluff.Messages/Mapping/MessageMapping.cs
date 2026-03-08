using BarkFluff.Proto.Files;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

using Proto.Shared;

public static class MessageMapping
{
    public static Message ToGrpc(this Domain.Message message)
    {
        return new Message
        {
            Id = message.Id,
            SentAt = Timestamp.FromDateTime(message.SentAt),
            ReadBy = { message.ReadBy },
            SenderId = message.SenderId,
            Content = message.Content?.ToGrpc(),
            Type = (MessageContentType)(int)message.Type
        };
    }

    public static Message ToGrpc(this Domain.Message message, Dictionary<string, UploadFileInfo> filesInfoMap)
    {
        return new Message
        {
            Id = message.Id,
            SentAt = Timestamp.FromDateTime(message.SentAt),
            ReadBy = { message.ReadBy },
            SenderId = message.SenderId,
            Content = message.Content?.ToGrpc(filesInfoMap),
            Type = (MessageContentType)(int)message.Type
        };
    }
}