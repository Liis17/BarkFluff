using BarkFluff.Proto.Messages;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using PinnedMessageInfo = BarkFluff.Proto.Shared.PinnedMessageInfo;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class MessengerService : IMessengerService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;

    public MessengerService(WebApiClient webApi, IClientSession session)
    {
        _webApi = webApi;
        _session = session;
    }

    public long? CurrentUserId => _session.CurrentConnection?.ConnectionParameters.UserId;

    public string CurrentNodeAddress => _session.CurrentConnection?.Profile.BeaconAddress ?? string.Empty;

    public Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.GetChats(parameters));

    public Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(
        string chatId,
        string text,
        long replyToMessageId = 0,
        IReadOnlyList<long>? forwardedMessageIds = null,
        CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.SendMessage(parameters, (false, chatId), new ForwardingLetter
        {
            Text = text,
            ReplyToMessageId = replyToMessageId,
            ForwardedMessageIds = forwardedMessageIds?.ToList() ?? [],
        }));

    public Task<(ErrorReturner error, MessageModel? message)> EditMessageAsync(string chatId, long messageId, string text, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.EditMessage(parameters, chatId, messageId, text));

    public Task<ErrorReturner> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.DeleteMessage(parameters, messageId));

    public Task<(ErrorReturner error, PinnedMessageInfo? pinned)> PinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.PinMessage(parameters, chatId, messageId));

    public Task<ErrorReturner> UnpinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.UnpinMessage(parameters, chatId, messageId));

    public async Task<(ErrorReturner error, List<PinnedMessageInfo>? pinned)> GetPinnedMessagesAsync(string chatId, CancellationToken cancellationToken = default)
    {
        var result = await WithParametersAsync(parameters => _webApi.ListPinnedMessages(parameters, chatId));
        return (result.error, result.pinned);
    }

    public Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessageAsync(string chatId, string text, byte[] key, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.SendPrivateMessage(chatId, text, key, parameters));

    public Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesAsync(string chatId, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.GetMessagesWithOffset(parameters, chatId, fromMessageId, offsetBefore, offsetAfter));

    public Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> GetPrivateMessagesAsync(string chatId, byte[] key, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.ListPrivateMessages(chatId, key, parameters, fromMessageId, offsetBefore, offsetAfter));

    public Task<ErrorReturner> MarkMessagesReadAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.MarkMessageAsRead(parameters, messageIds.ToList()));

    public Task<ErrorReturner> MarkPrivateMessagesReadAsync(string chatId, long lastReadMessageId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.MarkPrivateMessagesAsRead(chatId, lastReadMessageId, parameters));

    public Task<(ErrorReturner Error, UserData? Data)> GetUserDataAsync(long userId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.GetUserData(parameters, userId));

    public Task<(ErrorReturner error, List<UserData>? users)> SearchUsersAsync(string query, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.SearchUser(parameters, query));

    public async Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || _session.CurrentConnection?.ConnectionParameters is not { } parameters)
        {
            return string.Empty;
        }

        var result = await _webApi.GetFile(parameters, fileId);
        return result.error.IsSuccess ? result.url ?? string.Empty : string.Empty;
    }

    public byte[]? UnlockPrivateChat(Chat chat, string passphrase) => WebApiClient.UnlockPrivateChat(chat, passphrase);

    public Task<(ErrorReturner error, string chatId)> GetPersonChatIdAsync(long userId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.GetPersonChatId(parameters, userId));

    public Task<(ErrorReturner error, List<Proto.Messages.ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachmentsAsync(
        string chatId,
        Proto.Shared.MessageAttachmentType attachmentType,
        int offset,
        int size,
        CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.ListChatAttachments(parameters, chatId, attachmentType, sortDescending: true, offset, size));

    private async Task<T> WithParametersAsync<T>(Func<GlobalParam, Task<T>> operation)
    {
        if (_session.CurrentConnection?.ConnectionParameters is not { } parameters)
        {
            throw new InvalidOperationException("The node session is unavailable.");
        }

        return await operation(parameters);
    }
}
