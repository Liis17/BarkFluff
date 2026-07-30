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
        FakeOnlinePresenceService? presence = null) =>
        new(messenger ?? new FakeMessengerService(),
            new FakePrivateChatKeyStore(),
            realtime ?? new FakeRealtimeMessengerService(),
            presence ?? new FakeOnlinePresenceService(),
            new StubLocalizationService(),
            new InlineUiDispatcher());

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
    public Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class FakeRealtimeMessengerService : IRealtimeMessengerService
{
    public event EventHandler<IncomingMessage>? MessageReceived;
    public event EventHandler<MessageReadReceipt>? MessageRead;
    public event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    public void Raise(IncomingMessage message) => MessageReceived?.Invoke(this, message);

    public void Raise(MessageReadReceipt receipt) => MessageRead?.Invoke(this, receipt);

    public void Raise(PrivateMessageReadReceipt receipt) => PrivateMessageRead?.Invoke(this, receipt);

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeOnlinePresenceService : IOnlinePresenceService
{
    private readonly Dictionary<long, UserPresence> _known = [];

    public event EventHandler<UserPresence>? PresenceChanged;

    public List<long[]> WatchedSets { get; } = [];

    public void Seed(UserPresence presence) => _known[presence.UserId] = presence;

    public void Raise(UserPresence presence)
    {
        _known[presence.UserId] = presence;
        PresenceChanged?.Invoke(this, presence);
    }

    public UserPresence? TryGet(long userId) => _known.TryGetValue(userId, out var presence) ? presence : null;

    public Task WatchAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        WatchedSets.Add([.. userIds]);
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeMessengerService : IMessengerService
{
    public long? CurrentUserId { get; set; } = 1;

    public string CurrentNodeAddress => string.Empty;

    public List<Chat> Chats { get; } = [];

    public List<MessageModel> Messages { get; } = [];

    public UserData? UserData { get; set; }

    public bool UserDataFails { get; set; }

    public Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, List<Chat>?)>((new ErrorReturner(true), [.. Chats]));

    public Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(string chatId, string text, long forwardedMessageId = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult<(ErrorReturner, MessageModel?)>((new ErrorReturner(true), null));

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
        Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new ErrorReturner(true), [.. Messages.Where(message => message.ChatId == chatId)]));

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

    public Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public byte[]? UnlockPrivateChat(Chat chat, string passphrase) => null;
}
