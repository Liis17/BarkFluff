using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using PinnedMessageInfo = BarkFluff.Proto.Shared.PinnedMessageInfo;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class MessengerService : IMessengerService, IOutgoingMessageTransport
{
    private const int ChatPageSize = 50;
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
        WithParametersAsync(parameters => LoadChatsAsync(parameters, null, cancellationToken));

    public Task<(ErrorReturner error, List<Chat>? chats)> GetFirstChatPageAsync(CancellationToken cancellationToken) =>
        WithParametersAsync(async parameters =>
        {
            var (error, chats, _) = await _webApi.ListChatsPageAsync(parameters, 0, ChatPageSize, cancellationToken);
            return (error ?? new ErrorReturner(false), chats);
        });

    public async Task<Chat?> FindChatAsync(string chatId, CancellationToken cancellationToken = default)
    {
        if (_session.CurrentConnection?.ConnectionParameters is not { } parameters)
            return null;

        var result = await LoadChatsAsync(parameters, chatId, cancellationToken);
        return result.chats?.FirstOrDefault();
    }

    private Task<(ErrorReturner error, List<Chat>? chats)> LoadChatsAsync(
        GlobalParam parameters, string? targetId, CancellationToken cancellationToken) =>
        LoadChatPagesAsync((offset, size, token) =>
            _webApi.ListChatsPageAsync(parameters, offset, size, token), targetId, cancellationToken);

    internal static async Task<(ErrorReturner error, List<Chat>? chats)> LoadChatPagesAsync(
        Func<int, int, CancellationToken, Task<(ErrorReturner error, List<Chat>? chats, int totalCount)>> loadPage,
        string? targetId, CancellationToken cancellationToken)
    {
        var result = new List<Chat>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; ;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await loadPage(offset, ChatPageSize, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (page.error is not { IsSuccess: true } || page.chats is null)
                return (page.error is { IsSuccess: false } ? page.error : new ErrorReturner(false), null);

            foreach (var chat in page.chats)
            {
                if (targetId == chat.Id)
                    return (page.error, [chat]);
                if (targetId is null && ids.Add(chat.Id))
                    result.Add(chat);
            }

            offset += page.chats.Count;
            if (page.chats.Count == 0 || offset >= page.totalCount)
                return (page.error, result);
        }
    }

    public Task<ErrorReturner> SetChatMutedAsync(string chatId, bool muted, CancellationToken cancellationToken) =>
        WithParametersAsync(parameters => _webApi.SetChatMuted(chatId, muted, parameters, null, cancellationToken));

    public Task<(ErrorReturner error, List<BarkFluff.Proto.Users.MutedChat>? chats)> GetMutedChatsAsync(CancellationToken cancellationToken) =>
        WithParametersAsync(parameters => _webApi.GetMutedChats(parameters, cancellationToken));

    public Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(
        string chatId,
        string text,
        long replyToMessageId = 0,
        IReadOnlyList<long>? forwardedMessageIds = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachmentIds = null) =>
        WithParametersAsync(parameters => _webApi.SendMessage(parameters, (false, chatId), new ForwardingLetter
        {
            Text = text,
            ReplyToMessageId = replyToMessageId,
            ForwardedMessageIds = forwardedMessageIds?.ToList() ?? [],
            FilesId = attachmentIds?.ToList() ?? [],
        }, cancellationToken));

    public Task<long?> GetAttachmentStorageAvailableAsync(CancellationToken cancellationToken) =>
        WithParametersAsync(parameters => _webApi.GetAttachmentStorageAvailableAsync(parameters, cancellationToken));

    public Task<(ErrorReturner error, string? fileId)> UploadAttachmentAsync(string path, string name, string? contentType,
        UploadFileType type, IProgress<double> progress, CancellationToken cancellationToken) =>
        WithParametersAsync(parameters => Task.Run(
            () => _webApi.UploadFileAsync(parameters, path, name, contentType, type, progress, cancellationToken), cancellationToken));

    Task<long?> IOutgoingMessageTransport.GetStorageAvailableAsync(OutgoingScope scope, CancellationToken token) =>
        _webApi.GetOutgoingAttachmentStorageAvailableAsync(OutgoingParameters(scope, token), token);

    Task<(ErrorReturner error, string? fileId)> IOutgoingMessageTransport.UploadAsync(
        OutgoingScope scope, OutgoingFile file, string path, IProgress<double> progress, CancellationToken token) =>
        _webApi.UploadFileAsync(OutgoingParameters(scope, token), path, file.Name, file.ContentType, file.Type,
            file.Id, progress, token);

    Task<(ErrorReturner error, MessageModel? message)> IOutgoingMessageTransport.SendAsync(
        OutgoingScope scope, OutgoingMessage message, CancellationToken token) =>
        _webApi.SendMessage(OutgoingParameters(scope, token), message.ChatId, new ForwardingLetter
        {
            Text = message.Text,
            ReplyToMessageId = message.ReplyId,
            ForwardedMessageIds = [.. message.Forwards],
            FilesId = [.. message.Attachments, .. message.Uploaded.OfType<string>()],
        }, message.Id, token);

    private GlobalParam OutgoingParameters(OutgoingScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_session.CurrentConnection is { } connection && connection.ConnectionParameters.UserId is { } userId &&
            OutgoingScope.Create(connection.Profile.BeaconAddress, userId) == scope)
            return connection.ConnectionParameters;

        throw new OperationCanceledException();
    }

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
        return await WithParametersAsync<(ErrorReturner error, List<PinnedMessageInfo>? pinned)>(async parameters =>
        {
            List<PinnedMessageInfo> pinned = [];
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _webApi.ListPinnedMessages(parameters, chatId, pinned.Count);
                if (!result.error.IsSuccess || result.pinned is null)
                {
                    return (result.error, null);
                }

                pinned.AddRange(result.pinned);
                if (pinned.Count >= result.totalCount || result.pinned.Count == 0)
                {
                    return (result.error, pinned.DistinctBy(pin => pin.Message?.Id).ToList());
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (new ErrorReturner(false), null);
        });
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
        WithParametersAsync(parameters => _webApi.SearchUser(parameters, query, cancellationToken));

    public Task<(ErrorReturner error, SearchMessagesResponse? page)> SearchMessagesAsync(
        SearchMessagesRequest request, CancellationToken cancellationToken) =>
        WithParametersAsync(parameters => _webApi.SearchMessagesAsync(parameters, request, cancellationToken));

    public async Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || _session.CurrentConnection?.ConnectionParameters is not { } parameters)
        {
            return string.Empty;
        }

        var result = await _webApi.GetFile(parameters, fileId);
        return result.error is { IsSuccess: true } ? result.url ?? string.Empty : string.Empty;
    }

    public byte[]? UnlockPrivateChat(Chat chat, string passphrase) => WebApiClient.UnlockPrivateChat(chat, passphrase);

    public Task<(ErrorReturner error, string chatId)> GetPersonChatIdAsync(long userId, CancellationToken cancellationToken = default) =>
        WithParametersAsync(parameters => _webApi.GetPersonChatId(parameters, userId, cancellationToken));

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
