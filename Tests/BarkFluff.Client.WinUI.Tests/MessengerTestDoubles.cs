using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Client.WinUI.Tests;

/// <summary>
/// Общие дублёры для тестов мессенджера. Все методы завершаются синхронно, поэтому
/// «выстрелил и забыл» внутри вьюмодели успевает отработать до проверок.
/// </summary>
internal static class MessengerTestDoubles
{
    public static MessengerViewModel CreateViewModel(
        FakeMessengerService? messenger = null,
        FakeRealtimeMessengerService? realtime = null,
        FakeOnlinePresenceService? presence = null)
    {
        var effectiveMessenger = messenger ?? new FakeMessengerService();
        var effectivePresence = presence ?? new FakeOnlinePresenceService();
        var localization = new StubLocalizationService();
        return new(effectiveMessenger,
            new FakePrivateChatKeyStore(),
            realtime ?? new FakeRealtimeMessengerService(),
            effectivePresence,
            localization,
            new InlineUiDispatcher(),
            new ProfileViewModel(effectiveMessenger, effectivePresence, localization));
    }

    public static Chat CreateChat(string id, string title = "Chat", long peerUserId = 0, ChatType chatType = ChatType.Regular)
    {
        var chat = new Chat
        {
            Id = id,
            Title = title,
            ChatType = chatType,
            IsGroupChat = peerUserId == 0,
            LastActivityAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (peerUserId != 0)
        {
            chat.Members.Add(new ChatMember { UserId = peerUserId });
        }

        return chat;
    }

    public static MessageModel CreateMessage(long messageId, string chatId, long senderId, string text = "text") => new()
    {
        MessageId = messageId,
        ChatId = chatId,
        SenderId = senderId,
        Text = text,
        SentAt = Timestamp.FromDateTime(DateTime.UtcNow)
    };

    public static ChatAttachmentInfo CreateAttachmentInfo(long id, MessageAttachmentType type) => new()
    {
        AttachmentId = id,
        MessageId = id,
        SentAt = Timestamp.FromDateTime(DateTime.UtcNow),
        Attachment = new MessageAttachment
        {
            Id = id,
            Type = type,
            FileId = $"file-{id}",
            FileName = $"file-{id}.bin"
        }
    };
}

internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class StubLocalizationService : ILocalizationService
{
    public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
    public void Apply(string language) { }
    public string GetString(string resourceKey) => resourceKey;
}

internal sealed class FakePrivateChatKeyStore : IPrivateChatKeyStore
{
    public Action? OnForgetAll { get; set; }
    public Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public int ForgetAllCalls { get; private set; }

    public Task ForgetAllAsync(CancellationToken cancellationToken = default)
    {
        ForgetAllCalls++;
        OnForgetAll?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class FakeRealtimeMessengerService : IRealtimeMessengerService
{
    public Action? OnStop { get; set; }
    public event EventHandler<IncomingMessage>? MessageReceived;
    public event EventHandler<MessageReadReceipt>? MessageRead;
    public event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;
    public event EventHandler<bool>? ConnectionChanged;

    public void Raise(IncomingMessage message) => MessageReceived?.Invoke(this, message);

    public void Raise(MessageReadReceipt receipt) => MessageRead?.Invoke(this, receipt);

    public void Raise(PrivateMessageReadReceipt receipt) => PrivateMessageRead?.Invoke(this, receipt);

    public void RaiseConnection(bool isConnected) => ConnectionChanged?.Invoke(this, isConnected);

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync()
    {
        OnStop?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeOnlinePresenceService : IOnlinePresenceService
{
    private readonly Dictionary<long, UserPresence> _known = [];

    public event EventHandler<UserPresence>? PresenceChanged;
    public event EventHandler<bool>? ConnectionChanged;

    public List<long[]> WatchedSets { get; } = [];

    public Action? OnStop { get; set; }

    public void Seed(UserPresence presence) => _known[presence.UserId] = presence;

    public void Raise(UserPresence presence)
    {
        _known[presence.UserId] = presence;
        PresenceChanged?.Invoke(this, presence);
    }

    public void RaiseConnection(bool isConnected) => ConnectionChanged?.Invoke(this, isConnected);

    public UserPresence? TryGet(long userId) => _known.TryGetValue(userId, out var presence) ? presence : null;

    public Task WatchAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        WatchedSets.Add([.. userIds]);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        OnStop?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeMessengerService : IMessengerService
{
    public long? CurrentUserId { get; set; } = 1;

    public string CurrentNodeAddress => string.Empty;

    public List<Chat> Chats { get; } = [];

    public List<MessageModel> Messages { get; } = [];

    public UserData? UserData { get; set; }

    public List<UserData> SearchUsers { get; } = [];

    public List<string> SearchQueries { get; } = [];

    public bool SearchUsersFail { get; set; }

    public bool UserDataFails { get; set; }

    public bool ChatsFail { get; set; }

    public bool MessagesFail { get; set; }

    public bool SendFails { get; set; }

    public Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, List<Chat>?)>(ChatsFail
            ? (new ErrorReturner(false, "чаты недоступны"), null)
            : (new ErrorReturner(true), [.. Chats]));

    public Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(string chatId, string text, long replyToMessageId = 0, IReadOnlyList<long>? forwardedMessageIds = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, MessageModel?)>(SendFails
            ? (new ErrorReturner(false, "сообщение не отправлено"), null)
            : (new ErrorReturner(true), null));

    public Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessageAsync(string chatId, string text, byte[] key, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, PrivateMessageModel?)>((new ErrorReturner(true), null));

    public Task<(ErrorReturner error, MessageModel? message)> EditMessageAsync(string chatId, long messageId, string text, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, MessageModel?)>((new ErrorReturner(true), null));

    public Task<ErrorReturner> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner error, PinnedMessageInfo? pinned)> PinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, PinnedMessageInfo?)>((new ErrorReturner(true), null));

    public Task<ErrorReturner> UnpinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner error, List<PinnedMessageInfo>? pinned)> GetPinnedMessagesAsync(string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, List<PinnedMessageInfo>?)>((new ErrorReturner(true), []));

    public Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesAsync(string chatId, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, List<MessageModel>?)>(MessagesFail
            ? (new ErrorReturner(false, "история недоступна"), null)
            : (new ErrorReturner(true), [.. Messages.Where(message => message.ChatId == chatId)]));

    public Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> GetPrivateMessagesAsync(string chatId, byte[] key, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, List<PrivateMessageModel>?)>((new ErrorReturner(true), []));

    public Task<ErrorReturner> MarkMessagesReadAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<ErrorReturner> MarkPrivateMessagesReadAsync(string chatId, long lastReadMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner Error, UserData? Data)> GetUserDataAsync(long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, UserData?)>(UserDataFails
            ? (new ErrorReturner(false, "failed"), null)
            : (new ErrorReturner(true), UserData));

    public Task<(ErrorReturner error, List<UserData>? users)> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQueries.Add(query);
        return Task.FromResult<(ErrorReturner, List<UserData>?)>(SearchUsersFail
            ? (new ErrorReturner(false, "search failed"), null)
            : (new ErrorReturner(true), [.. SearchUsers]));
    }

    public Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public byte[]? UnlockPrivateChat(Chat chat, string passphrase) => null;

    public string? PersonChatId { get; set; }

    public bool PersonChatIdFails { get; set; }

    public Task<(ErrorReturner error, string chatId)> GetPersonChatIdAsync(long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, string)>(PersonChatIdFails
            ? (new ErrorReturner(false, "чат не найден"), string.Empty)
            : (new ErrorReturner(true), PersonChatId ?? string.Empty));

    public List<ChatAttachmentInfo> Attachments { get; } = [];

    public bool AttachmentsFail { get; set; }

    public Task<(ErrorReturner error, List<ChatAttachmentInfo>? attachments, int totalCount)> ListChatAttachmentsAsync(
        string chatId,
        MessageAttachmentType attachmentType,
        int offset,
        int size,
        CancellationToken cancellationToken = default)
    {
        if (AttachmentsFail)
        {
            return Task.FromResult<(ErrorReturner, List<ChatAttachmentInfo>?, int)>((new ErrorReturner(false, "вложения недоступны"), null, 0));
        }

        var filtered = Attachments.Where(info => info.Attachment.Type == attachmentType).ToList();
        var page = filtered.Skip(offset).Take(size).ToList();
        return Task.FromResult<(ErrorReturner, List<ChatAttachmentInfo>?, int)>((new ErrorReturner(true), page, filtered.Count));
    }
}
