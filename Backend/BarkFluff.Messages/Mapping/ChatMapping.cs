using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;

namespace BarkFluff.Messages.Mapping;

public static class ChatMapping
{
    public static Chat ToGrpc(this Domain.Chat chat)
    {
        return ToGrpc(chat, null);
    }

    public static Chat ToGrpc(this Domain.Chat chat, Dictionary<string, UploadFileInfo>? filesInfoMap)
    {
        return new Chat
        {
            Id = chat.Id.ToString(),
            CountUnread = chat.CountUnread,
            IsGroupChat = chat.IsGroupChat,
            LastMessage = chat.LastMessage?.ToGrpc(filesInfoMap ?? new Dictionary<string, UploadFileInfo>()),
            Picture = chat.Picture ?? string.Empty,
            Title = chat.Title,
            Members = { chat.Members?.Select(x => x.ToGrpc()) }
        };
    }
}