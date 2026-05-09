using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;

using Google.Protobuf;

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
            Title = chat.Title ?? string.Empty,
            Members = { chat.Members?.Select(x => x.ToGrpc()) },
            FirstUnreadMessageId = chat.FirstUnreadMessageId ?? 0,
            ChatType = (BarkFluff.Proto.Shared.ChatType)chat.Type,
            KdfSalt = chat.KdfSalt is { Length: > 0 } salt ? ByteString.CopyFrom(salt) : ByteString.Empty,
            PassphraseVerifier = chat.PassphraseVerifier is { Length: > 0 } v ? ByteString.CopyFrom(v) : ByteString.Empty,
        };
    }
}