using System.Collections.Specialized;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using OutgoingMessage = BarkFluff.Client.Core.Services.OutgoingMessage;

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
        FakeOnlinePresenceService? presence = null,
        FakePrivateChatKeyStore? keys = null,
        IUiDispatcher? dispatcher = null,
        TimeProvider? timeProvider = null,
        IChatFoldersState? chatFolders = null,
        IChatTypingService? typing = null,
        ILocalizationService? localization = null,
        MessageNotificationService? notifications = null,
        SettingsNotificationsViewModel? notificationSettings = null,
        StickerPickerViewModel? stickerPicker = null,
        ISettingsPreferences? stickerPreferences = null,
        IVoiceRecorder? voiceRecorder = null,
        IVoicePlayer? voicePlayer = null,
        IChatDraftStore? draftStore = null,
        IChatDraftRemote? draftRemote = null,
        OutgoingMessageQueue? outgoing = null,
        ChatListCache? chatCache = null,
        IAuthenticationService? authentication = null,
        IOnboardingNavigationService? navigation = null,
        SqliteApplicationDataStore? secretStore = null,
        IClientSession? clientSession = null)
    {
        var effectiveMessenger = messenger ?? new FakeMessengerService();
        var effectivePresence = presence ?? new FakeOnlinePresenceService();
        localization ??= new StubLocalizationService();
        outgoing ??= CreateOutgoingQueue(effectiveMessenger, timeProvider ?? TimeProvider.System);
        return new(effectiveMessenger,
            keys ?? new FakePrivateChatKeyStore(),
            realtime ?? new FakeRealtimeMessengerService(),
            effectivePresence,
            localization,
            dispatcher ?? new InlineUiDispatcher(),
            new ProfileViewModel(effectiveMessenger, effectivePresence, localization), outgoing,
            timeProvider, chatFolders, typing, notifications, notificationSettings, stickerPicker, stickerPreferences,
            voiceRecorder: voiceRecorder, voicePlayer: voicePlayer, draftStore: draftStore, draftRemote: draftRemote,
            chatCache: chatCache, authentication: authentication, navigation: navigation,
            secretStore: secretStore, clientSession: clientSession);
    }

    internal static OutgoingMessageQueue CreateOutgoingQueue(FakeMessengerService messenger, TimeProvider clock) =>
        new(messenger.OutgoingStore, messenger,
            new AppDataPaths(Path.Combine(Path.GetTempPath(), $"barkfluff-test-outgoing-{Guid.NewGuid():N}")), clock)
        {
            AfterEnqueueCommitAsync = message =>
                messenger.OutgoingStore.AfterFirstSaveAsync?.Invoke(message) ?? Task.CompletedTask
        };

    internal static Task WaitForMessageAsync(MessengerViewModel vm, long id, IUiDispatcher? dispatcher = null)
    {
        var messages = (INotifyCollectionChanged)vm.Messages;
        return ObserveAsync(() => vm.Messages.Any(row => row.Id == id),
            notify =>
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => notify();
                messages.CollectionChanged += handler;
                return () => messages.CollectionChanged -= handler;
            }, dispatcher);
    }

    internal static Task WaitForOutgoingAsync(MessengerViewModel vm, Func<bool> condition,
        IUiDispatcher? dispatcher = null) =>
        ObserveAsync(condition, notify =>
        {
            System.ComponentModel.PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName == nameof(MessengerViewModel.OutgoingHistoryNotice))
                    notify();
            };
            vm.PropertyChanged += handler;
            return () => vm.PropertyChanged -= handler;
        }, dispatcher);

    private static async Task ObserveAsync(Func<bool> condition, Func<Action, Action> subscribe,
        IUiDispatcher? dispatcher = null)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed()
        {
            if (condition())
                ready.TrySetResult();
        }
        var unsubscribe = subscribe(Changed);
        try
        {
            if (dispatcher is null)
                Changed();
            else
                dispatcher.Post(Changed);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { unsubscribe(); }
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

internal sealed class InlineUiDispatcher(bool serializeContinuations = false) : IUiDispatcher
{
    private readonly object _gate = new();

    public Task RunAsync(Func<Task> action)
    {
        Task task = Task.CompletedTask;
        Post(() => task = action());
        return task;
    }

    public void Post(Action action)
    {
        lock (_gate)
        {
            if (!serializeContinuations)
            {
                action();
                return;
            }
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherContext(this));
            try
            {
                action();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
    }

    private sealed class DispatcherContext(InlineUiDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            dispatcher.Post(() => callback(state));
    }
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
    public byte[]? Key { get; set; }
    public Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Key);

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
    public event EventHandler<MessageUpdate>? MessageChanged;
    public event EventHandler<bool>? ResyncRequested;
    public event EventHandler? ChatFoldersChanged;
    public event EventHandler<bool>? ConnectionChanged;

    public void Raise(IncomingMessage message) => MessageReceived?.Invoke(this, message);

    public void Raise(MessageUpdate update) => MessageChanged?.Invoke(this, update);

    public void SignalFoldersChanged() => ChatFoldersChanged?.Invoke(this, EventArgs.Empty);
    public void Resync(bool immediate = true) => ResyncRequested?.Invoke(this, immediate);

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

internal sealed class FakeMessengerService : IMessengerService, IOutgoingMessageTransport
{
    public long? CurrentUserId { get; set; } = 1;

    public string CurrentNodeAddress { get; set; } = string.Empty;

    public List<Chat> Chats { get; } = [];
    public Func<string, Task<Chat?>>? FindChat { get; set; }
    public Func<string, CancellationToken, Task<Chat?>>? OnFindChat { get; set; }

    public Task<Chat?> FindChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        OnFindChat?.Invoke(chatId, cancellationToken) ?? FindChat?.Invoke(chatId) ??
        Task.FromResult(Chats.FirstOrDefault(chat => chat.Id == chatId));

    public List<MessageModel> Messages { get; } = [];
    public List<PrivateMessageModel> PrivateMessages { get; } = [];
    public List<PinnedMessageInfo> Pins { get; } = [];
    public Func<string, long, Task<(ErrorReturner, List<MessageModel>?)>>? History { get; set; }
    public Func<string, long, int, int, Task<(ErrorReturner, List<MessageModel>?)>>? HistoryPage { get; set; }
    public Func<string, long, int, int, Task<(ErrorReturner, List<PrivateMessageModel>?)>>? PrivateHistoryPage { get; set; }
    public Func<string, Task<string>>? FileUrl { get; set; }
    public Func<Task<(ErrorReturner, List<PinnedMessageInfo>?)>>? PinnedHistory { get; set; }
    public Func<Task<(ErrorReturner, List<Chat>?)>>? ChatSnapshot { get; set; }
    public Func<string, bool, Task<ErrorReturner>>? ChangeMute { get; set; }
    public Func<Task<(ErrorReturner, List<BarkFluff.Proto.Users.MutedChat>?)>>? MuteSnapshot { get; set; }
    public List<BarkFluff.Proto.Users.MutedChat>? MutedChats { get; set; }

    public Task<ErrorReturner> SetChatMutedAsync(string chatId, bool muted, CancellationToken cancellationToken) =>
        ChangeMute?.Invoke(chatId, muted) ?? Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner error, List<BarkFluff.Proto.Users.MutedChat>? chats)> GetMutedChatsAsync(CancellationToken cancellationToken) =>
        MuteSnapshot?.Invoke() ?? Task.FromResult((new ErrorReturner(MutedChats is not null), MutedChats));

    public Func<long, Task<(ErrorReturner, UserData?)>>? UserProfile { get; set; }
    public UserData? UserData { get; set; }

    public List<UserData> SearchUsers { get; } = [];
    public Func<SearchMessagesRequest, CancellationToken, Task<(ErrorReturner, SearchMessagesResponse?)>>? OnSearchMessages { get; set; }

    public Task<(ErrorReturner error, SearchMessagesResponse? page)> SearchMessagesAsync(
        SearchMessagesRequest request, CancellationToken cancellationToken) =>
        OnSearchMessages?.Invoke(request, cancellationToken) ??
        Task.FromResult<(ErrorReturner, SearchMessagesResponse?)>((new(true), new()));


    public List<string> SearchQueries { get; } = [];

    public bool SearchUsersFail { get; set; }
    public Func<string, CancellationToken, Task<(ErrorReturner, List<UserData>?)>>? OnSearchPeople { get; set; }

    public bool UserDataFails { get; set; }

    public bool ChatsFail { get; set; }

    public bool MessagesFail { get; set; }

    internal MemoryOutgoingStore OutgoingStore { get; } = new();

    internal sealed class MemoryOutgoingStore : IOutgoingMessageStore
    {
        private readonly Dictionary<(OutgoingScope, Guid), OutgoingMessage> _messages = [];
        private readonly HashSet<(OutgoingScope, Guid)> _notificationReplies = [];
        public Action<OutgoingMessage>? BeforeFirstSave { get; set; }
        public Func<OutgoingMessage, Task>? AfterFirstSaveAsync { get; set; }
        public IReadOnlyList<OutgoingMessage> Load(OutgoingScope scope) =>
            _messages.Where(pair => pair.Key.Item1 == scope).Select(pair => pair.Value).ToArray();
        public IReadOnlyList<(OutgoingScope Scope, Guid Id)> ListOperations() => _messages.Keys.ToArray();
        public void Save(OutgoingScope scope, OutgoingMessage message)
        {
            if (!_messages.ContainsKey((scope, message.Id)))
                BeforeFirstSave?.Invoke(message);
            _messages[(scope, message.Id)] = message;
        }
        public void Remove(OutgoingScope scope, Guid id) => _messages.Remove((scope, id));
        public bool TrySaveNotificationReply(OutgoingScope scope, OutgoingMessage message)
        {
            if (_notificationReplies.Contains((scope, message.Id)))
                return false;
            Save(scope, message);
            return _notificationReplies.Add((scope, message.Id));
        }
        public void Clear(OutgoingScope scope)
        {
            foreach (var message in Load(scope))
                Remove(scope, message.Id);
        }
    }

    internal Func<OutgoingMessage, CancellationToken, Task<(ErrorReturner, MessageModel?)>>? SendOutgoing { get; set; }
    public Func<Task<(ErrorReturner, MessageModel?)>>? SendResponse { get; set; }
    public Func<Task<ErrorReturner>>? DeleteResponse { get; set; }

    public Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default) =>
        ChatSnapshot?.Invoke() ?? Task.FromResult<(ErrorReturner, List<Chat>?)>(ChatsFail
            ? (new ErrorReturner(false, "чаты недоступны"), null)
            : (new ErrorReturner(true), [.. Chats]));

    public Func<CancellationToken, Task<(ErrorReturner, List<Chat>?)>>? FirstChatPage { get; set; }

    public Task<(ErrorReturner error, List<Chat>? chats)> GetFirstChatPageAsync(CancellationToken cancellationToken) =>
        FirstChatPage?.Invoke(cancellationToken) ?? Task.FromResult<(ErrorReturner, List<Chat>?)>(ChatsFail
            ? (new ErrorReturner(false, "чаты недоступны"), null)
            : (new ErrorReturner(true), Chats.Take(50).ToList()));

    public Func<string, string, long, IReadOnlyList<string>, Task<(ErrorReturner, MessageModel?)>>? SendAttachments { get; set; }
    public Func<string, BarkFluff.Proto.Files.UploadFileType, IProgress<double>, CancellationToken, Task<(ErrorReturner, string?)>>? UploadAttachment { get; set; }
    public Func<CancellationToken, Task<long?>>? GetAttachmentStorageAvailable { get; set; }

    public Task<long?> GetAttachmentStorageAvailableAsync(CancellationToken cancellationToken) =>
        GetAttachmentStorageAvailable?.Invoke(cancellationToken) ?? Task.FromResult<long?>(null);

    public Task<(ErrorReturner error, string? fileId)> UploadAttachmentAsync(string path, string name, string? contentType,
        BarkFluff.Proto.Files.UploadFileType type, IProgress<double> progress, CancellationToken cancellationToken) =>
        UploadAttachment!(name, type, progress, cancellationToken);

    public Task<(ErrorReturner error, MessageModel? message)> SendMessageAsync(string chatId, string text, long replyToMessageId = 0, IReadOnlyList<long>? forwardedMessageIds = null, CancellationToken cancellationToken = default, IReadOnlyList<string>? attachmentIds = null) =>
        (attachmentIds is { Count: > 0 } ? SendAttachments?.Invoke(chatId, text, replyToMessageId, attachmentIds) : null)
        ?? SendResponse?.Invoke() ?? Task.FromResult<(ErrorReturner, MessageModel?)>((new ErrorReturner(true), null));

    Task<long?> IOutgoingMessageTransport.GetStorageAvailableAsync(OutgoingScope scope, CancellationToken token) =>
        GetAttachmentStorageAvailableAsync(token);

    Task<(ErrorReturner error, string? fileId)> IOutgoingMessageTransport.UploadAsync(OutgoingScope scope,
        OutgoingFile file, string path, IProgress<double> progress, CancellationToken token) =>
        UploadAttachmentAsync(path, file.Name, file.ContentType, file.Type, progress, token);

    async Task<(ErrorReturner error, MessageModel? message)> IOutgoingMessageTransport.SendAsync(
        OutgoingScope scope, OutgoingMessage message, CancellationToken token)
    {
        var (error, sent) = await (SendOutgoing?.Invoke(message, token) ??
            SendMessageAsync(message.ChatId, message.Text, message.ReplyId, message.Forwards, token,
                message.Attachments.Concat(message.Uploaded.OfType<string>()).ToArray()));
        if (sent is not null)
            sent.ClientOperationId = message.Id.ToString();
        if (error is { IsSuccess: true } && sent is null && SendResponse is null && SendOutgoing is null)
            return (new(false, errorResourceKey: "Messenger_UserUnavailable"), null);
        return (error, sent);
    }

    public Func<Task<(ErrorReturner, PrivateMessageModel?)>>? PrivateSendResponse { get; set; }
    public Func<Task<(ErrorReturner, MessageModel?)>>? EditResponse { get; set; }
    public Func<Task<(ErrorReturner, PinnedMessageInfo?)>>? PinResponse { get; set; }

    public Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessageAsync(string chatId, string text, byte[] key, CancellationToken cancellationToken = default) =>
        PrivateSendResponse?.Invoke() ?? Task.FromResult<(ErrorReturner, PrivateMessageModel?)>((new ErrorReturner(true), null));

    public Task<(ErrorReturner error, MessageModel? message)> EditMessageAsync(string chatId, long messageId, string text, CancellationToken cancellationToken = default) =>
        EditResponse?.Invoke() ?? Task.FromResult<(ErrorReturner, MessageModel?)>((new ErrorReturner(true), null));

    public Task<ErrorReturner> DeleteMessageAsync(long messageId, CancellationToken cancellationToken = default) =>
        DeleteResponse?.Invoke() ?? Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner error, PinnedMessageInfo? pinned)> PinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        PinResponse?.Invoke() ?? Task.FromResult<(ErrorReturner, PinnedMessageInfo?)>((new ErrorReturner(true), null));

    public Task<ErrorReturner> UnpinMessageAsync(string chatId, long messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner error, List<PinnedMessageInfo>? pinned)> GetPinnedMessagesAsync(string chatId, CancellationToken cancellationToken = default) =>
        PinnedHistory?.Invoke() ?? Task.FromResult<(ErrorReturner, List<PinnedMessageInfo>?)>((new ErrorReturner(true), [.. Pins]));

    public Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesAsync(string chatId, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        HistoryPage?.Invoke(chatId, fromMessageId, offsetBefore, offsetAfter) ?? History?.Invoke(chatId, fromMessageId) ?? Task.FromResult<(ErrorReturner, List<MessageModel>?)>(MessagesFail
            ? (new ErrorReturner(false, "история недоступна"), null)
            : (new ErrorReturner(true), [.. Messages.Where(message => message.ChatId == chatId)]));

    public Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> GetPrivateMessagesAsync(string chatId, byte[] key, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
        PrivateHistoryPage?.Invoke(chatId, fromMessageId, offsetBefore, offsetAfter) ??
        Task.FromResult<(ErrorReturner, List<PrivateMessageModel>?)>((new ErrorReturner(true), [.. PrivateMessages.Where(message => message.ChatId == chatId)]));

    public Func<IReadOnlyCollection<long>, Task<ErrorReturner>>? MarkReadResponse { get; set; }

    public Task<ErrorReturner> MarkMessagesReadAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
        MarkReadResponse?.Invoke(messageIds) ?? Task.FromResult(new ErrorReturner(true));

    public Task<ErrorReturner> MarkPrivateMessagesReadAsync(string chatId, long lastReadMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ErrorReturner(true));

    public Task<(ErrorReturner Error, UserData? Data)> GetUserDataAsync(long userId, CancellationToken cancellationToken = default) =>
        UserProfile?.Invoke(userId) ?? Task.FromResult<(ErrorReturner, UserData?)>(UserDataFails
            ? (new ErrorReturner(false, "failed"), null)
            : (new ErrorReturner(true), UserData));

    public Task<(ErrorReturner error, List<UserData>? users)> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQueries.Add(query);
        if (OnSearchPeople is not null)
            return OnSearchPeople(query, cancellationToken);
        return Task.FromResult<(ErrorReturner, List<UserData>?)>(SearchUsersFail
            ? (new ErrorReturner(false, "search failed"), null)
            : (new ErrorReturner(true), [.. SearchUsers]));
    }

    public Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default) =>
        FileUrl?.Invoke(fileId) ?? Task.FromResult(string.Empty);

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
