using BarkFluff.Proto.Files;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

using Proto.Shared;

public static class PinnedMessageMapping
{
    public static PinnedMessageInfo ToGrpc(this Domain.PinnedMessage pin, Domain.Message message,
        Dictionary<string, UploadFileInfo>? filesInfoMap = null,
        IReadOnlyDictionary<long, ReplyInfo>? replyPreviews = null)
    {
        return new PinnedMessageInfo
        {
            Message = filesInfoMap is null
                ? message.ToGrpc()
                : message.ToGrpc(filesInfoMap, replyPreviews: replyPreviews),
            PinnerUserId = pin.PinnerUserId,
            PinnedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(pin.PinnedAt, DateTimeKind.Utc))
        };
    }
}
