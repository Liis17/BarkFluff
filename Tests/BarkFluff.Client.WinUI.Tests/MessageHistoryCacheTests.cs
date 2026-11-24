using System.Text;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.Crypto;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessageHistoryCacheTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task PinInClosedChatOnlyShowsOfflineWhenHistoryHasTheMessage(long id, bool cached)
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        var row = Row("chat", 1);
        fixture.History.Write(scope, [Tail("chat", row), new("chat", [], Pins: [new PinnedMessageInfo
        {
            Message = new Message { Id = 10, SentAt = row.Regular!.SentAt, Content = new MessageContent { Text = "old pin" } }
        }])]);
        var history = new MessageHistoryCache(fixture.History);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "node" };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("chat"));
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: ui,
            chatCache: new ChatListCache(new ChatListCacheTests.MemoryCache(), history));
        Task loading = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        try
        {
            realtime.Raise(new MessageUpdate("chat", false, id, MessageUpdateKind.Pin, PinnedAt: new Timestamp { Seconds = 1 }));
            await history.FlushAsync();

            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(() =>
            {
                vm.IsOffline = true;
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.IsHistoryUpdating) && !vm.IsHistoryUpdating && vm.Messages.Count > 0)
                        applied.TrySetResult();
                };
                vm.SelectedChat = Assert.Single(vm.Chats);
            });
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ui.Post(() =>
            {
                if (cached)
                    Assert.Equal("cached text", vm.PinnedPreview?.Preview);
                else
                    Assert.Null(vm.PinnedPreview);
            });
        }
        finally
        {
            ui.Post(vm.Reset);
            await history.FlushAsync();
        }
    }

    [Fact]
    public async Task EventsWithoutConfirmedTailOnlyUpdateExistingRows()
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        var detached = Row("chat", 10);
        fixture.History.Write(scope, [
            new("chat", [detached], ((detached.SentAt, detached.Id), (detached.SentAt, detached.Id))),
            new("chat", [Row("chat", 10, "edited")]),
            new("chat", [Row("chat", 11)]),
            new("chat", [Row("chat", 12)])]);

        Assert.Equal("edited", Assert.Single(fixture.History.Read(scope, "chat", false, 10).Messages).Regular!.Text);
        Assert.Empty(fixture.History.Read(scope, "chat", false, 11).Messages);
        Assert.Empty(fixture.History.Read(scope, "chat", false, 12).Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrivateHistoryAndRealtimeCacheOnlyServerEnvelopes(bool hasPageEnvelope)
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        const string chatId = "private";
        var key = new byte[32];
        EncryptedMessage Envelope(long id)
        {
            var (ciphertext, nonce) = PrivateChatCrypto.Encrypt(Encoding.UTF8.GetBytes("private text"), key,
                PrivateChatCrypto.PrivateChatAad(chatId));
            return new()
            {
                Id = id, ChatId = chatId, SenderId = 2, SentAt = new Timestamp { Seconds = id },
                Ciphertext = ByteString.CopyFrom(ciphertext), Nonce = ByteString.CopyFrom(nonce)
            };
        }
        var page = Envelope(1);
        var incoming = Envelope(2);
        var history = new MessageHistoryCache(fixture.History);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "node" };
        var chat = MessengerTestDoubles.CreateChat(chatId, chatType: ChatType.Private);
        chat.PrivateInviteState = PrivateChatInviteState.Accepted;
        messenger.Chats.Add(chat);
        messenger.PrivateMessages.Add(hasPageEnvelope ? BarkFluff.WebApi.Core.WebApi.DecryptPrivateMessage(page, key) : new()
        {
            ChatId = chatId, MessageId = 1, SenderId = 2, SentAt = page.SentAt, Text = "private text"
        });
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime,
            keys: new FakePrivateChatKeyStore { Key = key }, dispatcher: ui,
            chatCache: new ChatListCache(new ChatListCacheTests.MemoryCache(), history));
        Task loading = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        try
        {
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(() =>
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.IsHistoryUpdating) && !vm.IsHistoryUpdating && vm.Messages.Count == 1)
                        applied.TrySetResult();
                };
                vm.SelectedChat = Assert.Single(vm.Chats);
            });
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ui.Post(() =>
            {
                Assert.Equal("private text", Assert.Single(vm.Messages).Text);
                if (hasPageEnvelope)
                {
                    realtime.Raise(new MessageUpdate(chatId, true, 2, MessageUpdateKind.New, Encrypted: incoming));
                    Assert.Equal("private text", vm.Messages[^1].Text);
                }
            });
            await history.FlushAsync();
            var rows = fixture.History.Read(scope, chatId, true, 0).Messages;
            if (hasPageEnvelope)
            {
                Assert.Equal(new[] { page, incoming }, rows.Select(row => row.Encrypted!));
                Assert.All(rows, row => Assert.Null(row.Regular));
            }
            else
            {
                Assert.Equal(0, fixture.History.Size(scope));
            }
        }
        finally
        {
            ui.Post(vm.Reset);
            await history.FlushAsync();
        }
    }

    internal static HistoryCacheWrite Tail(string chat, params CachedHistoryMessage[] rows) =>
        new(chat, rows, (rows.Length < 50 ? (DateTimeOffset.MinValue, 0L) : (rows[0].SentAt, rows[0].Id),
            (DateTimeOffset.MaxValue, long.MaxValue)));

    internal static CachedHistoryMessage Row(string chat, long id, string text = "cached text")
    {
        var message = MessengerTestDoubles.CreateMessage(id, chat, 2, text);
        message.SentAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddSeconds(id));
        return new(id, message.SentAt.ToDateTimeOffset(), message, null);
    }

    [Fact]
    public async Task WritesBoundedProtectedRowsAndSplitsAtCorruption()
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var store = fixture.History;
        var scope = ChatListScope.Create("node/", 1);
        var other = ChatListScope.Create("other", 1);
        var rows = Enumerable.Range(1, 1002).Select(id => Row("chat", id)).ToArray();
        store.Write(scope, [Tail("chat", rows)]);
        store.Write(other, [Tail("chat", Row("chat", 1))]);
        Assert.Empty(store.Read(ChatListScope.Create("node", 2), "chat", false, 0).Messages);
        var tail = store.Read(ChatListScope.Create("NODE", 1), "chat", false, 0);
        Assert.True(tail.IncludesTail);
        Assert.Equal(50, tail.Messages.Count);
        Assert.True(tail.HasOlder);
        Assert.Equal(953, tail.Messages[0].Id);
        Assert.Equal("cached text", tail.Messages[0].Regular!.Text);

        using var connection = new SqliteConnection($"Data Source={fixture.Paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM message_history_cache WHERE node = 'NODE';";
        Assert.Equal(1000L, command.ExecuteScalar());
        command.CommandText = "SELECT protected_data FROM message_history_cache WHERE node = 'NODE' AND message_id = 1001;";
        Assert.DoesNotContain("cached text", Encoding.UTF8.GetString((byte[])command.ExecuteScalar()!));
        command.CommandText = "UPDATE message_history_cache SET protected_data = X'00' WHERE node = 'NODE' AND message_id = 901;";
        command.ExecuteNonQuery();

        Assert.True(store.Read(scope, "chat", false, 0).IncludesTail);
        var window = store.Read(scope, "chat", false, 500);
        Assert.Equal(Enumerable.Range(450, 101).Select(id => (long)id), window.Messages.Select(row => row.Id));
        Assert.False(window.IncludesTail);
        Assert.True(window.HasOlder);
        command.CommandText = "SELECT COUNT(*) FROM message_history_cache WHERE node = 'NODE' AND message_id = 901;";
        Assert.Equal(1L, command.ExecuteScalar());
        var previous = store.Read(scope, "chat", false, 950, older: true);
        // 901 находится в запрошенной странице, но за пределами окна вокруг 500 и хвоста.
        Assert.Equal(Enumerable.Range(902, 49).Select(id => (long)id), previous.Messages.Select(row => row.Id));
        Assert.False(previous.HasOlder);
        Assert.False(previous.IncludesTail);

        command.CommandText = "UPDATE message_history_cache SET protected_data = X'00' WHERE node = 'NODE' AND message_id = 1001;";
        command.ExecuteNonQuery();

        Assert.Equal(1002, Assert.Single(store.Read(scope, "chat", false, 0).Messages).Id);
        Assert.Empty(store.Read(scope, "chat", false, 1001).Messages);
        var older = store.Read(scope, "chat", false, 1000);
        Assert.False(older.IncludesTail);
        Assert.Equal(51, older.Messages.Count);

        Assert.True(store.Clear(scope));
        Assert.Equal(0, store.Size(scope));
        Assert.Single(store.Read(other, "chat", false, 0).Messages);
        await fixture.Store.MarkWelcomeSeenAsync();
        command.CommandText = "UPDATE message_history_version SET version = 0;";
        command.ExecuteNonQuery();
        await fixture.Store.InitializeAsync();
        Assert.Empty(store.Read(other, "chat", false, 0).Messages);
        Assert.True(await fixture.Store.HasSeenWelcomeAsync());
    }

    [Fact]
    public async Task ScopeLimitAndDisconnectedPagesKeepSeparateRanges()
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        using var connection = new SqliteConnection($"Data Source={fixture.Paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE sequence(i) AS (
                SELECT 1 UNION ALL SELECT i + 1 FROM sequence WHERE i < 20000
            )
            INSERT INTO message_history_cache
            SELECT 'NODE', 1, 'chat-' || (i / 500), i, i, 0, '', X'00' FROM sequence;
            """;
        command.ExecuteNonQuery();
        var newest = Row("window", 100);
        var detached = Row("window", 10);
        fixture.History.Write(scope, [Tail("window", newest),
            new("window", [detached], ((detached.SentAt, detached.Id), (detached.SentAt, detached.Id)))]);

        Assert.Equal(100, Assert.Single(fixture.History.Read(scope, "window", false, 0).Messages).Id);
        var page = fixture.History.Read(scope, "window", false, 10);
        Assert.False(page.IncludesTail);
        Assert.Equal(10, Assert.Single(page.Messages).Id);
        command.CommandText = "SELECT COUNT(*) FROM message_history_cache WHERE node = 'NODE';";
        Assert.Equal(20000L, command.ExecuteScalar());
        command.CommandText = "SELECT MIN(sent_at) FROM message_history_cache WHERE node = 'NODE';";
        Assert.Equal(3L, command.ExecuteScalar());
    }

    [Fact]
    public async Task SecretChatHistoryAndEventsAreExcluded()
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        var history = new MessageHistoryCache(fixture.History);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "node" };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("secret", chatType: ChatType.Secret));
        messenger.Messages.Add(Row("secret", 1).Regular!);
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: ui,
            chatCache: new ChatListCache(new ChatListCacheTests.MemoryCache(), history));
        Task loading = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        ui.Post(() => vm.SelectedChat = Assert.Single(vm.Chats));
        realtime.Raise(new MessageUpdate("secret", false, 2, MessageUpdateKind.New,
            Message: Row("secret", 2).Regular));
        ui.Post(() =>
        {
            Assert.Equal(new long[] { 1, 2 }, vm.Messages.Select(item => item.Id));
            vm.Reset();
        });
        await history.FlushAsync();
        Assert.Equal(0, fixture.History.Size(scope));
    }

    [Fact]
    public async Task ClearFinishesAfterBackgroundWriteAndRejectsOldGeneration()
    {
        var store = new BlockingHistoryStore();
        var cache = new MessageHistoryCache(store);
        var scope = ChatListScope.Create("node", 1);
        var generation = cache.Generation(scope);
        cache.Schedule(scope, generation, Tail("chat", Row("chat", 1)));
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var clear = cache.ClearAsync(scope);
            cache.Schedule(scope, generation, Tail("chat", Row("chat", 2)));
            Assert.False(clear.IsCompleted);
            store.Release.TrySetResult();
            Assert.True(await clear.WaitAsync(TimeSpan.FromSeconds(5)));
            await cache.FlushAsync();
            Assert.Equal(1, store.Writes);
            Assert.Equal(0, await cache.SizeAsync(scope));
        }
        finally
        {
            store.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task PrivatePayloadContainsOnlyTheServerEnvelope()
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        const string chat = "private";
        var encrypted = new EncryptedMessage
        {
            Id = 1, ChatId = chat, SentAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
            Ciphertext = ByteString.CopyFromUtf8("ciphertext"), SenderId = 2
        };
        // Даже если вызывающий передал обычную модель, хранилище выбирает только конверт.
        var row = new CachedHistoryMessage(1, DateTimeOffset.UnixEpoch,
            MessengerTestDoubles.CreateMessage(1, chat, 2, "private plaintext"), encrypted);
        fixture.History.Write(scope, [Tail(chat, row)]);
        var loaded = Assert.Single(fixture.History.Read(scope, chat, true, 0).Messages);
        Assert.Null(loaded.Regular);
        Assert.Equal(encrypted, loaded.Encrypted);
        Assert.True(fixture.History.Size(scope) > 0);
    }

    private sealed class BlockingHistoryStore : IMessageHistoryCacheStore
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Writes { get; private set; }
        private long _count;

        public CachedHistoryPage Read(ChatListScope scope, string chatId, bool isPrivate, long anchor, int pageSize, bool older) =>
            new([], false);
        public void Write(ChatListScope scope, IReadOnlyList<HistoryCacheWrite> writes)
        {
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            Writes++;
            _count += writes.Count;
        }
        public bool Clear(ChatListScope scope)
        {
            _count = 0;
            return true;
        }
        public long Size(ChatListScope scope) => _count;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheIsShownBeforeNetworkAndTailReplacementUsesMessageIdentity(bool overlaps)
    {
        await using var fixture = await HistoryDatabase.OpenAsync();
        var scope = ChatListScope.Create("node", 1);
        var old = Enumerable.Range(1, 50).Select(id => Row("chat", id)).ToArray();
        fixture.History.Write(scope, [Tail("chat", old)]);
        var history = new MessageHistoryCache(fixture.History);
        var cache = new ChatListCache(new ChatListCacheTests.MemoryCache(), history);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "node" };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("chat"));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        messenger.History = (_, _) =>
        {
            requested.TrySetResult();
            return response.Task;
        };
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: ui, chatCache: cache);
        Task loading = null!;
        ui.Post(() => loading = vm.LoadAsync());
        await loading;
        ui.Post(() => vm.SelectedChat = Assert.Single(vm.Chats));
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        BarkFluff.Client.Core.ViewModels.MessageItemViewModel retained = null!;
        ui.Post(() =>
        {
            retained = vm.Messages[^1];
            Assert.Equal(50, retained.Id);
        });

        var first = overlaps ? 50 : 100;
        var replacement = Enumerable.Range(first, 50).Select(id => Row("chat", id).Regular!).ToList();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsHistoryUpdating) && !vm.IsHistoryUpdating)
                applied.TrySetResult();
        });
        try
        {
            if (overlaps)
            {
                var edited = Row("chat", 50, "edited").Regular!;
                edited.IsEdited = true;
                edited.EditedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddDays(1));
                realtime.Raise(new MessageUpdate("chat", false, 50, MessageUpdateKind.Edit, Message: edited));
                realtime.Raise(new MessageUpdate("chat", false, 51, MessageUpdateKind.Delete));
            }

            response.SetResult((new ErrorReturner(true), replacement));
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task flushing = null!;
            ui.Post(() =>
            {
                Assert.Equal(replacement.Select(row => row.MessageId).Where(id => !overlaps || id != 51),
                    vm.Messages.Select(item => item.Id));
                if (overlaps)
                {
                    Assert.Same(retained, vm.Messages[0]);
                    Assert.Equal("edited", retained.Text);
                }
                flushing = vm.FlushChatCacheAsync();
            });
            await flushing;

            var cached = fixture.History.Read(scope, "chat", false, 0).Messages;
            if (overlaps)
            {
                Assert.DoesNotContain(cached, row => row.Id == 51);
                Assert.Equal("edited", cached.Single(row => row.Id == 50).Regular!.Text);
                realtime.Raise(new MessageUpdate("chat", false, 100, MessageUpdateKind.New,
                    Message: Row("chat", 100, "realtime").Regular));
                await history.FlushAsync();
                Assert.Equal("realtime", fixture.History.Read(scope, "chat", false, 0).Messages[^1].Regular!.Text);
            }
            else
            {
                Assert.Equal(replacement.Select(row => row.MessageId), cached.Select(row => row.Id));
            }
        }
        finally
        {
            ui.Post(vm.Reset);
            await history.FlushAsync();
        }
    }

    internal sealed class HistoryDatabase : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        internal AppDataPaths Paths { get; }
        internal SqliteApplicationDataStore Store { get; }
        internal IMessageHistoryCacheStore History => Store;

        private HistoryDatabase()
        {
            Paths = new AppDataPaths(_directory);
            Store = new SqliteApplicationDataStore(Paths);
        }

        internal static async Task<HistoryDatabase> OpenAsync()
        {
            var database = new HistoryDatabase();
            await database.Store.InitializeAsync();
            return database;
        }

        public ValueTask DisposeAsync()
        {
            using var connection = new SqliteConnection($"Data Source={Paths.DatabasePath}");
            SqliteConnection.ClearPool(connection);
            using var shared = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString());
            SqliteConnection.ClearPool(shared);
            Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}

