using System.Collections.Concurrent;
using System.IO;
using System.Text;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static BarkFluff.Client.WinUI.Tests.MessengerTestDoubles;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ChatDraftTests
{
    [Fact]
    public async Task TypingCoalescesLocalWritesAndLeavingFlushesLatestDraft()
    {
        var store = new MemoryStore();
        var clock = new FakeTimeProvider();
        var vm = await OpenAsync(store, new Remote { Offline = true }, clock);
        var writes = new ConcurrentQueue<string>();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        store.Saved = draft => writes.Enqueue(draft.Text);

        vm.DraftText = "a";
        vm.DraftText = "ab";
        vm.DraftText = "abc";
        Assert.Equal("abc", vm.DraftText);
        Assert.Empty(writes);
        store.Saved = draft =>
        {
            writes.Enqueue(draft.Text);
            saved.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        try
        {
            await Task.Run(() => vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b"))
                .WaitAsync(TimeSpan.FromSeconds(5));
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "abc" }, writes);
        }
        finally
        {
            release.Set();
            await vm.FlushDraftsAsync();
        }
        vm.Reset();
    }

    [Fact]
    public async Task DraftFacadeAcceptsUserIdAssignedAfterClientCreation()
    {
        using var api = new BarkFluff.WebApi.Core.WebApi();
        const string address = "http://127.0.0.1:1";
        var parameters = new GlobalParam
        {
            SocketBeacon = address, SocketUsers = address, SocketIdentity = address,
            SocketFiles = address, SocketMessages = address, SocketUpdates = address,
            SocketOnliner = address, SocketFastAuth = address, DeviceId = "test-device"
        };
        Assert.True(api.CreateAC(parameters, "test", "test", "test", "1", "").IsSuccess);
        parameters.UserId = 42;

        // Отмена подтверждает, что вызов прошёл проверку привязки, без сетевого запроса.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            api.GetChatDraft(parameters, "a", new CancellationToken(true)));
        var session = new DraftClientSession(new(new NodeProfile(address, "", ""), parameters));
        var remote = new ChatDraftRemote(api, session);
        Assert.Null(await remote.GetAsync((address, 41), "a", new CancellationToken(true)));
        Assert.Null(await remote.GetAsync(("other-node", 42), "a", new CancellationToken(true)));
    }

    [Fact]
    public async Task RejectedDraftStaysLocalAndDoesNotRetryAfterRestart()
    {
        var store = new MemoryStore();
        var remote = new Remote
        {
            UpsertResponse = () => throw new Grpc.Core.RpcException(new(Grpc.Core.StatusCode.FailedPrecondition, ""))
        };
        var clock = new FakeTimeProvider();
        var vm = await OpenAsync(store, remote, clock);
        vm.DraftText = "keep locally";
        clock.Advance(TimeSpan.FromSeconds(2));
        vm.Reset();
        await vm.StopDraftsAsync();
        Assert.False(store.Load("node", 1)["a"].Pending);

        vm = await OpenAsync(store, remote, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("keep locally", vm.DraftText);
        Assert.Equal(1, remote.Upserts);
        Assert.Equal(1, remote.Gets);
        vm.Reset();
    }

    [Fact]
    public async Task UpsertRetriesWithoutReplyAfterTargetIsDeletedRemotely()
    {
        var messenger = new FakeMessengerService();
        messenger.Messages.Add(CreateMessage(10, "a", 1, "target"));
        var remote = new Remote { MissingReply = true };
        var store = new MemoryStore();
        var clock = new FakeTimeProvider();
        var vm = await OpenAsync(store, remote, clock, messenger);
        vm.StartReplyMessageCommand.Execute(Assert.Single(vm.Messages));
        vm.DraftText = "keep the text";

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(vm.ReplyTarget);
        clock.Advance(TimeSpan.FromSeconds(2));
        await vm.FlushDraftsAsync();
        Assert.Equal(2, remote.Upserts);
        Assert.Equal(0L, remote.Drafts["a"].ReplyToMessageId);
        Assert.Equal("keep the text", store.Load("node", 1)["a"].Text);
        Assert.False(store.Load("node", 1)["a"].Pending);
        vm.Reset();
    }

    [Fact]
    public async Task SqliteFailureLeavesDraftAvailableInSession()
    {
        using var database = await DraftDatabase.CreateAsync();
        using (var connection = new SqliteConnection($"Data Source={database.Paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE chat_drafts;";
            command.ExecuteNonQuery();
        }

        var vm = await OpenAsync(database.Store, new Remote { Offline = true }, new());
        vm.DraftText = "in memory";
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        await vm.FlushDraftsAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        Assert.Equal("in memory", vm.DraftText);
        vm.Reset();
        await vm.StopDraftsAsync();
    }

    [Fact]
    public async Task OfflineDraftSurvivesChatSwitchAndRestartAndSyncsOnRetry()
    {
        using var database = await DraftDatabase.CreateAsync();
        var remote = new Remote { Offline = true };
        var clock = new FakeTimeProvider();
        var first = await OpenAsync(database.Store, remote, clock);
        first.DraftText = "offline";
        first.SelectedChat = first.Chats.Single(chat => chat.Id == "b");
        Assert.Empty(first.DraftText);
        first.SelectedChat = first.Chats.Single(chat => chat.Id == "a");
        Assert.Equal("offline", first.DraftText);
        first.Reset();
        await first.StopDraftsAsync();

        var reopened = await OpenAsync(new SqliteApplicationDataStore(database.Paths), remote, clock);
        Assert.Equal("offline", reopened.DraftText);
        Assert.Contains("offline", reopened.SelectedChat!.ListPreview);
        remote.Offline = false;
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal("offline", remote.Drafts["a"].Text);
        await reopened.FlushDraftsAsync();
        Assert.False(database.Store.Load("node", 1)["a"].Pending);
        reopened.Reset();
    }

    [Theory]
    [InlineData("from another device", false)]
    [InlineData("sent", true)]
    public async Task ConditionalDeletePreservesDifferentDraftAndRetriesMatchingDraft(string latest, bool deleted)
    {
        using var database = await DraftDatabase.CreateAsync();
        var remote = new Remote();
        remote.Drafts["a"] = new() { Text = "sent", Revision = "original" };
        var vm = await OpenAsync(database.Store, remote, new(), new FakeMessengerService
        {
            SendResponse = () => Task.FromResult<(ErrorReturner, MessageModel?)>(
                (new(true), CreateMessage(100, "a", 1, "sent")))
        });

        remote.Drafts["a"] = new() { Text = latest, Revision = "other-device" };
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(deleted ? "" : latest, vm.DraftText);
        Assert.Equal(deleted ? new[] { "original", "other-device" } : new[] { "original" }, remote.DeleteRevisions);
        Assert.Equal(!deleted, remote.Drafts.ContainsKey("a"));
        vm.Reset();
        await vm.StopDraftsAsync();
    }

    [Fact]
    public async Task LateUpsertAcknowledgesRevisionWithoutReplacingNewInput()
    {
        var clock = new FakeTimeProvider();
        var store = new MemoryStore();
        var delayed = new TaskCompletionSource<UpsertChatDraftResponse?>();
        var remote = new Remote { UpsertResponse = () => delayed.Task };
        var vm = await OpenAsync(store, remote, clock);
        vm.DraftText = "old";
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, remote.Upserts);

        vm.DraftText = "new";
        var stored = new TaskCompletionSource<ChatDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Saved = draft =>
        {
            if (draft.Revision == "old-revision")
                stored.TrySetResult(draft);
        };
        delayed.SetResult(new() { Draft = new() { Text = "old", Revision = "old-revision" } });

        var current = await stored.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("new", current.Text);
        Assert.True(current.Pending);
        Assert.Equal("new", vm.DraftText);
        vm.Reset();
    }

    [Fact]
    public async Task SendPreservesNewGenerationEvenWhenUserTypesSameTextAgainAndEditRestoresDraft()
    {
        using var database = await DraftDatabase.CreateAsync();
        var remote = new Remote();
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new InlineUiDispatcher();
        var messenger = new FakeMessengerService
        {
            EditResponse = () => Task.FromResult<(ErrorReturner, MessageModel?)>(
                (new(true), CreateMessage(10, "a", 1, "edited")))
        };
        messenger.OutgoingStore.AfterFirstSaveAsync = _ =>
        {
            writing.SetResult();
            return release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        messenger.Messages.Add(CreateMessage(10, "a", 1, "message"));
        var vm = await OpenAsync(database.Store, remote, new(), messenger, dispatcher);
        vm.DraftText = "draft";
        var pending = vm.SendCommand.ExecuteAsync(null);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dispatcher.Post(() =>
        {
            vm.DraftText = "new";
            vm.DraftText = "draft";
        });
        release.SetResult();
        await pending;
        Assert.Equal("draft", vm.DraftText);
        Assert.Empty(remote.DeleteRevisions);

        vm.StartEditMessageCommand.Execute(vm.Messages.Single(message => message.Id == 10));
        vm.DraftText = "edited";
        await vm.FlushDraftsAsync();
        Assert.Equal("draft", database.Store.Load("node", 1)["a"].Text);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal("draft", vm.DraftText);
        dispatcher.Post(vm.Reset);
        await vm.StopDraftsAsync();
    }

    [Fact]
    public async Task SendCompletingAfterChatSwitchClearsOnlyOriginalChat()
    {
        using var database = await DraftDatabase.CreateAsync();
        var remote = new Remote();
        remote.Drafts["a"] = new() { Text = "send from a", Revision = "original" };
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messenger = new FakeMessengerService();
        var dispatcher = new InlineUiDispatcher();
        messenger.OutgoingStore.AfterFirstSaveAsync = _ =>
        {
            writing.SetResult();
            return release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var vm = await OpenAsync(database.Store, remote, new(), messenger, dispatcher);

        var pending = vm.SendCommand.ExecuteAsync(null);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dispatcher.Post(() =>
        {
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            vm.DraftText = "draft in b";
        });
        release.SetResult();
        await pending;

        Assert.Equal("draft in b", vm.DraftText);
        await vm.FlushDraftsAsync();
        Assert.Equal(new[] { "original" }, remote.DeleteRevisions);
        Assert.False(database.Store.Load("node", 1).ContainsKey("a"));
        Assert.Equal("draft in b", database.Store.Load("node", 1)["b"].Text);
        dispatcher.Post(vm.Reset);
        await vm.StopDraftsAsync();
        await vm.StopDraftsAsync();
    }

    [Fact]
    public async Task ReplyRestoresFromHistoryAndMissingTargetRestoresOnlyText()
    {
        var remote = new Remote();
        remote.Drafts["a"] = new() { Text = "reply draft", ReplyToMessageId = 10, Revision = "original" };
        var messenger = new FakeMessengerService();
        messenger.Messages.Add(CreateMessage(10, "a", 1, "target"));
        var store = new MemoryStore();
        var vm = await OpenAsync(store, remote, new(), messenger);
        Assert.Equal(10L, vm.ReplyTarget?.Id);
        vm.Reset();
        await vm.StopDraftsAsync();

        messenger.Messages.Clear();
        vm = await OpenAsync(store, remote, new(), messenger);
        Assert.Equal("reply draft", vm.DraftText);
        Assert.Null(vm.ReplyTarget);
        await vm.FlushDraftsAsync();
        Assert.Equal(0L, store.Load("node", 1)["a"].ReplyId);
        vm.Reset();
    }

    [Theory]
    [InlineData(ChatType.Private)]
    [InlineData(ChatType.Secret)]
    public async Task EncryptedDraftIsLocalAndScopedToNodeAndUser(ChatType type)
    {
        using var database = await DraftDatabase.CreateAsync();
        var remote = new Remote();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "node" };
        messenger.Chats.Add(CreateChat("a", chatType: type));
        var clock = new FakeTimeProvider();
        var vm = await OpenAsync(database.Store, remote, clock, messenger);
        const string text = "private draft marker";
        vm.DraftText = text;
        clock.Advance(TimeSpan.FromMinutes(1));
        vm.Reset();
        await vm.StopDraftsAsync();

        Assert.Equal(0, remote.Gets + remote.Upserts + remote.DeleteRevisions.Count);
        Assert.Empty(database.Store.Load("other-node", 1));
        Assert.Empty(database.Store.Load("node", 2));
        Assert.Equal(text, database.Store.Load("node", 1)["a"].Text);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.Paths.DatabasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_data FROM chat_drafts;";
        var bytes = Assert.IsType<byte[]>(command.ExecuteScalar());
        Assert.DoesNotContain(text, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task DebounceSendsOnlyLastInputAndUnimplementedStopsRequests()
    {
        var clock = new FakeTimeProvider();
        var remote = new Remote();
        var vm = await OpenAsync(new MemoryStore(), remote, clock);
        vm.DraftText = "a";
        clock.Advance(TimeSpan.FromSeconds(1));
        vm.DraftText = "ab";
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, remote.Upserts);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, remote.Upserts);
        Assert.Equal("ab", remote.Drafts["a"].Text);
        vm.Reset();

        remote = new Remote { Unsupported = true };
        vm = await OpenAsync(new MemoryStore(), remote, clock);
        vm.DraftText = "local";
        clock.Advance(TimeSpan.FromMinutes(1));
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        Assert.Equal(1, remote.Gets);
        Assert.Equal(0, remote.Upserts);
        vm.Reset();
    }

    private static async Task<MessengerViewModel> OpenAsync(IChatDraftStore store, Remote remote, FakeTimeProvider clock,
        FakeMessengerService? messenger = null, InlineUiDispatcher? dispatcher = null)
    {
        messenger ??= new();
        messenger.CurrentNodeAddress = "node";
        if (messenger.Chats.Count == 0)
            messenger.Chats.AddRange([CreateChat("a"), CreateChat("b")]);
        var outgoing = CreateOutgoingQueue(messenger, clock);
        var vm = CreateViewModel(messenger: messenger, dispatcher: dispatcher, timeProvider: clock,
            draftStore: store, draftRemote: remote, outgoing: outgoing);

        // Дождаться первого чтения общих часов очередью перед запуском таймеров черновика.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOutgoingChanged() => started.TrySetResult();
        outgoing.Changed += OnOutgoingChanged;
        try
        {
            await vm.LoadAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            outgoing.Changed -= OnOutgoingChanged;
        }
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        return vm;
    }

    internal sealed class Remote : IChatDraftRemote
    {
        public Dictionary<string, ChatDraftInfo> Drafts { get; } = [];
        public List<string> DeleteRevisions { get; } = [];
        public bool Offline { get; set; }
        public bool Unsupported { get; init; }
        public bool MissingReply { get; init; }
        public int Gets { get; private set; }
        public int Upserts { get; private set; }
        public Func<Task<UpsertChatDraftResponse?>>? UpsertResponse { get; init; }

        public Task<GetChatDraftResponse?> GetAsync((string Node, long UserId) scope, string chatId, CancellationToken token)
        {
            Gets++;
            if (Unsupported)
                throw new Grpc.Core.RpcException(new(Grpc.Core.StatusCode.Unimplemented, ""));
            return Task.FromResult<GetChatDraftResponse?>(Offline ? null : new() { Draft = Drafts.GetValueOrDefault(chatId) });
        }

        public Task<UpsertChatDraftResponse?> UpsertAsync((string Node, long UserId) scope, string chatId, string text, long replyId, CancellationToken token)
        {
            Upserts++;
            if (MissingReply && replyId != 0)
                throw new MessageNotFoundException();
            if (UpsertResponse is not null)
                return UpsertResponse();
            if (Offline)
                return Task.FromResult<UpsertChatDraftResponse?>(null);
            var draft = new ChatDraftInfo { Text = text, ReplyToMessageId = replyId, Revision = Guid.NewGuid().ToString() };
            Drafts[chatId] = draft;
            return Task.FromResult<UpsertChatDraftResponse?>(new() { Draft = draft });
        }

        public Task<DeleteChatDraftResponse?> DeleteAsync((string Node, long UserId) scope, string chatId, string revision, CancellationToken token)
        {
            DeleteRevisions.Add(revision);
            var deleted = Drafts.GetValueOrDefault(chatId)?.Revision == revision && Drafts.Remove(chatId);
            return Task.FromResult<DeleteChatDraftResponse?>(new() { Deleted = deleted });
        }
    }

    private sealed class MemoryStore : IChatDraftStore
    {
        private readonly ConcurrentDictionary<string, ChatDraft> _drafts = new();
        public Action<ChatDraft>? Saved { get; set; }

        public Dictionary<string, ChatDraft> Load(string node, long userId) => new(_drafts);

        public void RemoveNonRegularDrafts(string node, long userId)
        {
            foreach (var (id, draft) in _drafts)
                if (draft.Type != ChatType.Regular)
                    _drafts.TryRemove(id, out _);
        }

        public void Save(string node, long userId, string chatId, ChatDraft draft)
        {
            _drafts[chatId] = draft;
            Saved?.Invoke(draft);
        }
    }

    private sealed class DraftClientSession(NodeConnection connection) : IClientSession
    {
        public NodeConnection? CurrentConnection { get; private set; } = connection;
        public void SetConnection(NodeConnection value) => CurrentConnection = value;
    }

    internal sealed class DraftDatabase(string directory) : IDisposable
    {
        public AppDataPaths Paths { get; } = new(directory);
        public SqliteApplicationDataStore Store => new(Paths);

        public static async Task<DraftDatabase> CreateAsync()
        {
            var database = new DraftDatabase(Path.Combine(Path.GetTempPath(), "BarkFluff-drafts-" + Guid.NewGuid()));
            await database.Store.InitializeAsync();
            return database;
        }

        public void Dispose()
        {
            SqliteConnection.ClearPool(new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString()));
            Directory.Delete(directory, recursive: true);
        }
    }
}
