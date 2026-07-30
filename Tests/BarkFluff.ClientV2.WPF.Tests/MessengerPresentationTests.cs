using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Infrastructure.Storage;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

using Microsoft.Data.Sqlite;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class MessengerPresentationTests
{
    [Fact]
    public void ChatItemViewModel_TrimsPreviewToTwentyTextElements()
    {
        var chat = new Chat
        {
            Id = "chat",
            Title = "Chat",
            LastMessage = new Message
            {
                Content = new MessageContent { Text = "one\n two   three four five six" }
            }
        };

        var item = new ChatItemViewModel(chat, "Chat", string.Empty, string.Empty, string.Empty);

        Assert.Equal("one two three four…", item.Preview);
        Assert.True(System.Globalization.StringInfo.ParseCombiningCharacters(item.Preview).Length <= 20);
    }

    [Fact]
    public void ChatItemViewModel_HidesPrivatePreview()
    {
        var chat = new Chat
        {
            Id = "private",
            Title = "Private",
            ChatType = ChatType.Private,
            LastMessage = new Message { Content = new MessageContent { Text = "hidden" } }
        };

        var item = new ChatItemViewModel(chat, "Private", string.Empty, string.Empty, string.Empty);

        Assert.False(item.HasPreview);
        Assert.Equal(string.Empty, item.Preview);
    }

    [Fact]
    public void MessageItemViewModel_SeparatesMediaAndFiles()
    {
        var media = new MessageAttachmentItemViewModel(MessageAttachmentType.Image, "preview", "file", "image.jpg", 1024, 800, 600);
        var document = new MessageAttachmentItemViewModel(MessageAttachmentType.Document, string.Empty, "file", "report.pdf", 2048, 0, 0);
        var message = new MessageItemViewModel(
            CreateMessengerViewModel(),
            new MessageModel
            {
                MessageId = 1,
                SenderId = 2,
                SentAt = Timestamp.FromDateTime(DateTime.UtcNow)
            },
            isMine: false,
            [media, document],
            currentUserId: 1,
            forwarded: null);

        Assert.True(message.HasMedia);
        Assert.True(message.HasFiles);
        Assert.False(message.HasOnlyMedia);
        Assert.Single(message.MediaAttachments);
        Assert.Single(message.FileAttachments);
    }

    [Fact]
    public async Task DpapiPrivateChatKeyStore_PersistsKeysByNodeUserAndChat()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var dataStore = new SqliteApplicationDataStore(new AppDataPaths(directory));
            await dataStore.InitializeAsync();
            var keyStore = new DpapiPrivateChatKeyStore(dataStore);
            var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

            await keyStore.SaveAsync("https://node.example.com", 42, "chat", key);

            var restored = await new DpapiPrivateChatKeyStore(dataStore)
                .TryGetAsync("https://node.example.com", 42, "chat");
            Assert.Equal(key, restored);
            Assert.Null(await keyStore.TryGetAsync("https://node.example.com", 43, "chat"));

            await keyStore.ForgetAsync("https://node.example.com", 42, "chat");
            Assert.Null(await keyStore.TryGetAsync("https://node.example.com", 42, "chat"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static MessengerViewModel CreateMessengerViewModel() => new(
        new FakeMessengerService(),
        new FakePrivateChatKeyStore(),
        new FakeRealtimeMessengerService(),
        new TestLocalizationService());

    private sealed class FakeMessengerService : IMessengerService
    {
        public long? CurrentUserId => 1;

        public string CurrentNodeAddress => string.Empty;

        public Task<(ErrorReturner error, List<Chat>? chats)> GetChatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<(ErrorReturner, List<Chat>?)>((new ErrorReturner(true), []));

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
            Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new ErrorReturner(true), []));

        public Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> GetPrivateMessagesAsync(string chatId, byte[] key, long fromMessageId, int offsetBefore, int offsetAfter, CancellationToken cancellationToken = default) =>
            Task.FromResult<(ErrorReturner, List<PrivateMessageModel>?)>((new ErrorReturner(true), []));

        public Task<ErrorReturner> MarkMessagesReadAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ErrorReturner(true));

        public Task<ErrorReturner> MarkPrivateMessagesReadAsync(string chatId, long lastReadMessageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ErrorReturner(true));

        public Task<(ErrorReturner Error, UserData? Data)> GetUserDataAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(ErrorReturner, UserData?)>((new ErrorReturner(true), null));

        public Task<string> ResolveFileUrlAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public byte[]? UnlockPrivateChat(Chat chat, string passphrase) => null;
    }

    private sealed class FakePrivateChatKeyStore : IPrivateChatKeyStore
    {
        public Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRealtimeMessengerService : IRealtimeMessengerService
    {
        public event EventHandler<MessageReadReceipt>? MessageRead
        {
            add { }
            remove { }
        }

        public event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
