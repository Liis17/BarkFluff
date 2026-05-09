using BarkFluff.Proto.Shared;

using Google.Protobuf;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

public static class EncryptedMessageMapping
{
    public static EncryptedMessage ToGrpc(this Domain.EncryptedMessage message)
    {
        var grpc = new EncryptedMessage
        {
            Id = message.Id,
            ChatId = message.ChatId.ToString(),
            SenderId = message.SenderId,
            SenderDeviceId = message.SenderDeviceId.ToString(),
            SentAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc)),
            Ciphertext = ByteString.CopyFrom(message.Ciphertext),
            Nonce = ByteString.CopyFrom(message.Nonce),
            AssociatedData = ByteString.CopyFrom(message.AssociatedData),
            IsEdited = message.IsEdited,
            IsDeleted = message.IsDeleted,
        };

        if (message.EditedAt.HasValue)
        {
            grpc.EditedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(message.EditedAt.Value, DateTimeKind.Utc));
        }

        return grpc;
    }
}
