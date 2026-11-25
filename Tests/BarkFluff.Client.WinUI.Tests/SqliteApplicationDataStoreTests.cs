using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SqliteApplicationDataStoreTests
{
    [Fact]
    public async Task ChatSoundPersistsByNodeUserAndChatAndDefaultDeletesPreference()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var store = new SqliteApplicationDataStore(paths);
            await store.InitializeAsync();
            var target = new MessageNotificationTarget("node", 1, "chat");
            await ((IChatNotificationSoundStore)store).SaveAsync(target, ChatNotificationSound.Mail, default);

            var restarted = new SqliteApplicationDataStore(paths);
            IChatNotificationSoundStore sounds = restarted;
            Assert.Equal(ChatNotificationSound.Mail, await sounds.LoadAsync(target, default));
            Assert.Equal(ChatNotificationSound.Default, await sounds.LoadAsync(target with { Node = "other" }, default));
            Assert.Equal(ChatNotificationSound.Default, await sounds.LoadAsync(target with { UserId = 2 }, default));
            Assert.Equal(ChatNotificationSound.Default, await sounds.LoadAsync(target with { ChatId = "other" }, default));

            await sounds.SaveAsync(target, ChatNotificationSound.Default, default);
            Assert.Equal(ChatNotificationSound.Default, await sounds.LoadAsync(target, default));
            Assert.Null(await restarted.GetPreferenceAsync("notifications.chat-sound." + MessageNotificationPayload.Group(target)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSelectedNodeAsync_PersistsNodeAndSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SqliteApplicationDataStore(new AppDataPaths(directory));
            await store.InitializeAsync();
            await store.MarkWelcomeSeenAsync();
            await store.SaveLanguageAsync("ru");
            await store.SaveThemeAsync(ApplicationThemeMode.BarkFluffDark);
            await store.SaveSelectedNodeAsync(new NodeProfile("https://node.example.com", "Node", "Description"));
            await store.SaveNodeServiceConfigurationAsync(new NodeConnection(
                new NodeProfile("https://node.example.com", "Node", "Description"),
                new BarkFluff.WebApi.Core.MessengerData.GlobalParam
                {
                    SocketIdentity = "https://identity.example.com:443",
                    SocketUsers = "https://users.example.com:443"
                }));

            Assert.True(await store.HasSeenWelcomeAsync());
            Assert.Equal("ru", await store.GetLanguageAsync());
            Assert.Equal(ApplicationThemeMode.BarkFluffDark, await store.GetThemeAsync());
            Assert.Equal("Node", (await store.GetSelectedNodeAsync())!.Name);
            Assert.Equal("https://identity.example.com:443", (await store.GetNodeServiceConfigurationAsync())!.ConnectionParameters.SocketIdentity);
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
