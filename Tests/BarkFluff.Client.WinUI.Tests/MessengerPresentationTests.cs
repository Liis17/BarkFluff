using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.WinUI.Tests;

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

        var item = CreateChatItem(chat);

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

        var item = CreateChatItem(chat);

        Assert.False(item.HasPreview);
        Assert.Equal(string.Empty, item.Preview);
    }

    [Fact]
    public void ApplyIncomingMessage_SetsFirstUnreadOnlyOnce()
    {
        var item = CreateChatItem(MessengerTestDoubles.CreateChat("chat", peerUserId: 2));

        item.ApplyIncomingMessage(10, "hello", DateTimeOffset.UtcNow, countAsUnread: true);
        item.ApplyIncomingMessage(11, "world", DateTimeOffset.UtcNow, countAsUnread: true);

        Assert.Equal(2, item.UnreadCount);
        Assert.Equal(10, item.FirstUnreadMessageId);
        Assert.Equal("world", item.Preview);
    }

    [Fact]
    public void MessageItemViewModel_SeparatesMediaAndFiles()
    {
        var media = new MessageAttachmentItemViewModel(MessageAttachmentType.Image, "preview", "file", "image.jpg", 1024, 800, 600);
        var document = new MessageAttachmentItemViewModel(MessageAttachmentType.Document, string.Empty, "file", "report.pdf", 2048, 0, 0);
        var message = new MessageItemViewModel(
            MessengerTestDoubles.CreateViewModel(),
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

    private static ChatItemViewModel CreateChatItem(Chat chat) =>
        new(chat, chat.Title, string.Empty, string.Empty, string.Empty, null, new StubLocalizationService());
}
