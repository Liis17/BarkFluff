using BarkFluff.ClientV2.WPF.Infrastructure.Storage;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
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
            new MessageModel
            {
                MessageId = 1,
                SenderId = 2,
                SentAt = Timestamp.FromDateTime(DateTime.UtcNow)
            },
            isMine: false,
            [media, document],
            currentUserId: 1);

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
}
