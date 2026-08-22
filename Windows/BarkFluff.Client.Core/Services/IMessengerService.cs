using BarkFluff.Proto.Messages;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using PinnedMessageInfo = BarkFluff.Proto.Shared.PinnedMessageInfo;

namespace BarkFluff.Client.Core.Services;

public interface IMessengerService
{
    long? CurrentUserId { get; }

    string CurrentNodeAddress { get; }

    Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(
        string chatId,
        string text,
        long replyToMessageId = 0,
        IReadOnlyList<long>? forwardedMessageIds = null,
        CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, MessageModel? message)> EditMessageAsync(string chatId, long messageId, string text, CancellationToken cancellationToken = default);

    Task<ErrorReturner> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, PinnedMessageInfo? pinned)> PinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default);

    Task<ErrorReturner> UnpinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, List<PinnedMessageInfo>? pinned)> GetPinnedMessagesAsync(string chatId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessageAsync(string chatId, string text, byte[] key, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesAsync(string chatId, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> GetPrivateMessagesAsync(string chatId, byte[] key, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default);

    Task<ErrorReturner> MarkMessagesReadAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default);

    Task<ErrorReturner> MarkPrivateMessagesReadAsync(string chatId, long lastReadMessageId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner Error, UserData? Data)> GetUserDataAsync(long userId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, List<UserData>? users)> SearchUsersAsync(string query, CancellationToken cancellationToken = default);

    Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, string chatId)> GetPersonChatIdAsync(long userId, CancellationToken cancellationToken = default);

    Task<(ErrorReturner error, List<BarkFluff.Proto.Messages.ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachmentsAsync(
        string chatId,
        BarkFluff.Proto.Shared.MessageAttachmentType attachmentType,
        int offset,
        int size,
        CancellationToken cancellationToken = default);

    byte[]? UnlockPrivateChat(Chat chat, string passphrase);
}
