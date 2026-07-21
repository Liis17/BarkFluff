using BarkFluff.Proto.Messages;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Messages.Mapping;

public static class ChatMemberMapping
{
    public static ChatMember ToGrpc(this Domain.ChatMember chatMember)
    {
        var grpc = new ChatMember
        {
            UserId = chatMember.UserId ?? 0,
            JoinedAt = Timestamp.FromDateTime(chatMember.JoinedAt)
        };

        if (chatMember.UserUuid.HasValue)
            grpc.UserUuid = chatMember.UserUuid.Value.ToString();

        if (!string.IsNullOrEmpty(chatMember.ServerName))
            grpc.ServerName = chatMember.ServerName;

        return grpc;
    }
}