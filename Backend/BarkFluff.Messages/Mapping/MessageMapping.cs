using BarkFluff.Proto.Files;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

using Proto.Shared;

public static class MessageMapping
{
    public static Message ToGrpc(this Domain.Message message, IReadOnlyList<Guid>? federatedReadBy = null)
    {
        var grpc = new Message
        {
            Id = message.Id,
            SentAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc)),
            ReadBy = { message.ReadBy },
            SenderId = message.SenderId ?? 0,
            Content = message.Content?.ToGrpc(),
            Type = (MessageContentType)(int)message.Type,
            IsEdited = message.IsEdited
        };

        if (message.EditedAt.HasValue)
        {
            grpc.EditedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.EditedAt.Value, DateTimeKind.Utc));
        }

        if (message.FederatedId.HasValue)
            grpc.FederatedId = message.FederatedId.Value.ToString();

        if (message.SenderUuid.HasValue)
            grpc.SenderUuid = message.SenderUuid.Value.ToString();

        if (federatedReadBy is { Count: > 0 })
            grpc.FederatedReadBy.Add(federatedReadBy.Select(u => u.ToString()));

        return grpc;
    }

    public static Message ToGrpc(this Domain.Message message, Dictionary<string, UploadFileInfo> filesInfoMap, IReadOnlyList<Guid>? federatedReadBy = null)
    {
        var grpc = new Message
        {
            Id = message.Id,
            SentAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc)),
            ReadBy = { message.ReadBy },
            SenderId = message.SenderId ?? 0,
            Content = message.Content?.ToGrpc(filesInfoMap),
            Type = (MessageContentType)(int)message.Type,
            IsEdited = message.IsEdited
        };

        if (message.EditedAt.HasValue)
        {
            grpc.EditedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.EditedAt.Value, DateTimeKind.Utc));
        }

        if (message.FederatedId.HasValue)
            grpc.FederatedId = message.FederatedId.Value.ToString();

        if (message.SenderUuid.HasValue)
            grpc.SenderUuid = message.SenderUuid.Value.ToString();

        if (federatedReadBy is { Count: > 0 })
            grpc.FederatedReadBy.Add(federatedReadBy.Select(u => u.ToString()));

        return grpc;
    }
}
