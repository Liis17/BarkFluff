using System.Text;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using Google.Protobuf;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ChatListCacheTests
{
    [Fact]
    public async Task CacheIsScopedAndProtectedAndRecoversFromDamagedRowAndSchemaVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(directory);
            var store = new SqliteApplicationDataStore(paths);
            await store.InitializeAsync();
            await store.MarkWelcomeSeenAsync();
            var cache = (IChatListCacheStore)store;
            var current = ChatListScope.Create("https://node/", 1);
            var differentUser = ChatListScope.Create("https://node", 2);
            var differentNode = ChatListScope.Create("https://other", 1);
            var snapshot = new CachedChatList([Row("one", "private cached name"), Row("two", "Two")],
                [new ChatFolderData { FolderId = "folder", FolderName = "private folder" }]);
            cache.Save(current, snapshot);
            Assert.Empty(cache.Load(differentUser).Chats);
            Assert.Empty(cache.Load(differentNode).Chats);
            cache.Save(differentUser, snapshot);
            Assert.Equal(2, cache.Load(current).Chats.Count);
            Assert.Single(cache.Load(current).Folders);
            Assert.True(cache.Size(current) > 0);

            using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT protected_data FROM chat_list_cache WHERE kind = 0 AND position = 0 LIMIT 1;";
                var blob = (byte[])command.ExecuteScalar()!;
                Assert.DoesNotContain("private cached name", Encoding.UTF8.GetString(blob));
                command.CommandText = "UPDATE chat_list_cache SET protected_data = X'00' WHERE user_id = 1 AND kind = 0 AND position = 0;";
                command.ExecuteNonQuery();
                Assert.Equal("two", Assert.Single(cache.Load(current).Chats).Id);
                command.CommandText = "SELECT COUNT(*) FROM chat_list_cache WHERE user_id = 1 AND kind = 0 AND position = 0;";
                Assert.Equal(0L, command.ExecuteScalar());
                Assert.True(cache.Clear(current));
                Assert.Empty(cache.Load(current).Folders);
                Assert.Equal(2, cache.Load(differentUser).Chats.Count);
                command.CommandText = "UPDATE chat_list_cache_version SET version = 2;";
                command.ExecuteNonQuery();
            }

            await store.InitializeAsync();
            Assert.Empty(cache.Load(differentUser).Chats);
            Assert.True(await store.HasSeenWelcomeAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealtimeWritesInBackgroundAndClearWinsOverPendingSave()
    {
        var memory = new MemoryCache();
        var cache = new ChatListCache(memory);
        var ui = new InlineUiDispatcher();
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("one"));
        var realtime = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: ui, chatCache: cache);
        Task loading = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        await memory.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        memory.BeforeSave = list =>
        {
            if (list.Chats.Single().Preview == "new preview")
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }
        };

        var producer = Task.Run(() =>
            realtime.Raise(new IncomingMessage("one", MessengerTestDoubles.CreateMessage(10, "one", 2, "new preview"))));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Обработчик и диспетчер освобождаются, пока запись ещё заблокирована.
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
            Task<bool> clearing = null!;
            ui.Post(() =>
            {
                clearing = vm.ClearChatCacheAsync();
                vm.Reset();
            });
            release.TrySetResult();
            Assert.True(await clearing.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(memory.Snapshot.Chats);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task ClearingDuringOnlineSession_WaitsForNextFullSnapshotBeforeSavingAgain()
    {
        var memory = new MemoryCache();
        var ui = new InlineUiDispatcher();
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("one"));
        var realtime = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: ui,
            chatCache: new ChatListCache(memory));
        Task loading = null!;
        Task flushing = null!;
        Task<bool> clearing = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        ui.Post(() => flushing = vm.FlushChatCacheAsync());
        await flushing;
        Assert.Single(memory.Snapshot.Chats);
        ui.Post(() => clearing = vm.ClearChatCacheAsync());
        Assert.True(await clearing);

        realtime.Raise(new IncomingMessage("one", MessengerTestDoubles.CreateMessage(10, "one", 2, "new preview")));
        ui.Post(() =>
        {
            Assert.Equal("new preview", Assert.Single(vm.Chats).Preview);
            flushing = vm.FlushChatCacheAsync();
        });
        await flushing;
        Assert.Empty(memory.Snapshot.Chats);

        messenger.Chats.Add(MessengerTestDoubles.CreateChat("two"));
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        ui.Post(() =>
        {
            flushing = vm.FlushChatCacheAsync();
            vm.Reset();
        });
        await flushing;
        Assert.Equal(new[] { "one", "two" }, memory.Snapshot.Chats.Select(row => row.Id).Order());
    }

    internal static CachedChat Row(string id, string title) =>
        new(id, MessengerTestDoubles.CreateChat(id, title).ToByteArray(), title, "", "", "", null,
            AccountStatus.Active, "", 0, null, null);

    internal sealed class MemoryCache : IChatListCacheStore
    {
        public CachedChatList Snapshot { get; set; } = new([], []);
        public ChatListScope? Cleared { get; private set; }
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<CachedChatList>? BeforeSave { get; set; }
        public CachedChatList Load(ChatListScope scope) => Snapshot;
        public void Save(ChatListScope scope, CachedChatList list)
        {
            BeforeSave?.Invoke(list);
            Snapshot = list;
            Saved.TrySetResult();
        }
        public bool Clear(ChatListScope scope)
        {
            Cleared = scope;
            Snapshot = new([], []);
            return true;
        }
        public long Size(ChatListScope scope) => 0;
    }

    internal sealed class MemoryDrafts : IChatDraftStore
    {
        public Dictionary<string, ChatDraft> Load(string node, long userId) =>
            new() { ["kept"] = new("saved draft", 0, ChatType.Regular) };
        public void Save(string node, long userId, string chatId, ChatDraft draft) { }
        public void RemoveNonRegularDrafts(string node, long userId) { }
    }
}
