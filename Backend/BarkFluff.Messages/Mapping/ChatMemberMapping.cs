using BarkFluff.Proto.Messages;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

public static class ChatMemberMapping
{
    public static ChatMember ToGrpc(this Domain.ChatMember chatMember)
    {
        return new ChatMember
        {
            UserId = chatMember.UserId,
            JoinedAt = Timestamp.FromDateTime(chatMember.JoinedAt)
        };
    }
}