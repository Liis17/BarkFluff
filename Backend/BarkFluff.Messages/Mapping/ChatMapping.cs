using BarkFluff.Proto.Messages;

namespace BarkFluff.Messages.Mapping;

public static class ChatMapping
{
    public static Chat ToGrpc(this Domain.Chat chat)
    {
        return new Chat
        {
            Id = chat.Id.ToString(),
            CountUnread = chat.CountUnread,
            IsGroupChat = chat.IsGroupChat,
            LastMessage = chat.LastMessage?.ToGrpc(),
            Picture = chat.Picture,
            Title = chat.Title,
            Members = { chat.Members?.Select(x => x.ToGrpc()) }
        };
    }
}