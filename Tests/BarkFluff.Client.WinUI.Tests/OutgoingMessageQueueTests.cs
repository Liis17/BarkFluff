using System.Collections.Concurrent;
using System.Threading.Channels;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Microsoft.Extensions.Time.Testing;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class OutgoingMessageQueueTests : IDisposable
{
    private static readonly OutgoingScope Scope = OutgoingScope.Create("https://node.invalid", 1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"barkfluff-outbox-test-{Guid.NewGuid():N}");
    private readonly List<OutgoingMessageQueue> _queues = [];
    private readonly FakeTimeProvider _clock = new();

    private AppDataPaths Paths => new(_root);
    private IOutgoingMessageStore Store => new SqliteApplicationDataStore(Paths);

    [Fact]
    public async Task NotificationReplyWaitsForQueueAndIsDeduplicatedAfterRestart()
    {
        await InitializeAsync();
        var messenger = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        IReadOnlyCollection<long>? markedRead = null;
        messenger.MarkReadResponse = ids =>
        {
            markedRead = ids;
            return Task.FromResult(new ErrorReturner(true));
        };
        using var lifetime = new CancellationTokenSource();
        var queue = Start(new Transport(), start: false);
        var presenter = new ReplyPresenter();
        var notifications = new MessageNotificationService(presenter, new StubLocalizationService());
        var service = new NotificationReplyService(messenger, queue, notifications);
        var reply = new NotificationReply(new(Scope.Node, Scope.UserId, "a"), Guid.NewGuid(), 42, " \tответ\r\n ");
        var ready = Signal();

        var pending = service.AcceptAsync(reply, null, ready.Task, () => true, lifetime.Token);
        Assert.False(pending.IsCompleted);
        await queue.StartAsync(Scope, lifetime.Token, paused: true);
        ready.SetResult();
        Assert.Null(await pending);
        var saved = Assert.Single(Store.Load(Scope));
        Assert.Equal(reply.OperationId, saved.Id);
        Assert.Equal("ответ", saved.Text);
        Assert.Equal(reply, Assert.Single(presenter.Removed));
        Assert.Equal(new long[] { 42 }, markedRead);

        await queue.StopAsync();
        Store.Remove(Scope, saved.Id);
        var restarted = Start(new Transport(), start: false);
        await restarted.StartAsync(Scope, lifetime.Token, paused: true);
        service = new NotificationReplyService(messenger, restarted, notifications);
        Assert.Null(await service.AcceptAsync(reply, null, Task.CompletedTask, () => true, lifetime.Token));
        Assert.Empty(Store.Load(Scope));
    }

    [Fact]
    public async Task NotificationReplyRejectsWhitespaceBeforeWaiting()
    {
        await InitializeAsync();
        var messenger = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        var presenter = new ReplyPresenter();
        var service = new NotificationReplyService(messenger, Start(new Transport(), start: false),
            new MessageNotificationService(presenter, new StubLocalizationService()));
        var reply = new NotificationReply(new(Scope.Node, Scope.UserId, "a"), Guid.NewGuid(), 42, " \t\r\n ");

        Assert.Equal("Notifications_ReplyEmpty",
            await service.AcceptAsync(reply, null, Signal().Task, () => true, default).WaitAsync(Timeout));
        Assert.Empty(Store.Load(Scope));
        Assert.Empty(presenter.Removed);
    }

    [Fact]
    public async Task ClearRemovesOnlyCurrentScopeNotificationReplyReceipts()
    {
        await InitializeAsync();
        var store = Store;
        var otherUser = Scope with { UserId = 2 };
        var otherNode = OutgoingScope.Create("https://other.invalid", Scope.UserId);
        var message = new OutgoingMessage(Guid.NewGuid(), "a", "reply", 0, [], [], [], []);
        Assert.True(store.TrySaveNotificationReply(Scope, message));
        Assert.True(store.TrySaveNotificationReply(otherUser, message));
        Assert.True(store.TrySaveNotificationReply(otherNode, message));

        store.Clear(Scope);

        Assert.Empty(store.Load(Scope));
        Assert.True(store.TrySaveNotificationReply(Scope, message));
        Assert.False(store.TrySaveNotificationReply(otherUser, message));
        Assert.False(store.TrySaveNotificationReply(otherNode, message));
        Assert.Single(store.Load(otherUser));
        Assert.Single(store.Load(otherNode));
    }

    [Theory]
    [InlineData("other-node", 1)]
    [InlineData("https://node.invalid", 2)]
    public async Task NotificationReplyRejectsDifferentSessionBeforeWaiting(string node, long userId)
    {
        await InitializeAsync();
        var messenger = new FakeMessengerService { CurrentNodeAddress = "https://node.invalid" };
        var queue = Start(new Transport(), start: false);
        var notifications = new MessageNotificationService(new ReplyPresenter(), new StubLocalizationService());
        var service = new NotificationReplyService(messenger, queue, notifications);
        var reply = new NotificationReply(new(node, userId, "a"), Guid.NewGuid(), 42, "reply");

        Assert.Equal("Notifications_ReplySessionMismatch",
            await service.AcceptAsync(reply, null, Signal().Task, () => true, default).WaitAsync(Timeout));
        Assert.Empty(Store.Load(Scope));
    }

    [Fact]
    public async Task NotificationReplyRejectsSessionChangeWhileWaiting()
    {
        var messenger = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        var queue = Start(new Transport(), start: false);
        var service = new NotificationReplyService(messenger, queue,
            new MessageNotificationService(new ReplyPresenter(), new StubLocalizationService()));
        var reply = new NotificationReply(new(Scope.Node, Scope.UserId, "a"), Guid.NewGuid(), 42, "reply");
        var ready = Signal();
        using var session = new CancellationTokenSource();
        var pending = service.AcceptAsync(reply, null, ready.Task, () => true, session.Token);

        await session.CancelAsync();

        Assert.Equal("Notifications_ReplySessionMismatch", await pending.WaitAsync(Timeout));
    }

    private sealed class ReplyPresenter : INotificationPresenter
    {
        public List<NotificationReply> Removed { get; } = [];
        public void Show(MessageNotification notification) { }
        public void PreviewSound(ChatNotificationSound sound, string text) { }
        public void RemoveChat(MessageNotificationTarget target) { }
        public void RemoveAll() { }
        public void RemoveReply(NotificationReply reply) => Removed.Add(reply);
    }

    [Fact]
    public async Task DatabaseReadFailureDoesNotDisableEnqueueForSession()
    {
        await InitializeAsync();
        var store = new RemovalThreadStore(Store) { FailLoad = true };
        var queue = new OutgoingMessageQueue(store, new Transport(send: (_, token) => NeverSendAsync(token)), Paths, _clock);
        _queues.Add(queue);
        await queue.StartAsync(Scope);

        await EnqueueAsync(queue, "a", "queued after read error");

        Assert.Equal("queued after read error", Assert.Single(Store.Load(Scope)).Text);
    }

    [Fact]
    public async Task ForcedLogoutClearsStagingForInvalidatedAccount()
    {
        await InitializeAsync();
        var service = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        service.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        var queue = Start(new Transport(upload: async (_, _, token) =>
        {
            await NeverSendAsync(token);
            return default;
        }), start: false);
        var vm = MessengerTestDoubles.CreateViewModel(service, outgoing: queue);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        var file = Path.Combine(_root, "composer.bin");
        File.WriteAllText(file, "attachment");
        await vm.AddAttachmentsAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>([new(file, "file.bin")]));
        await vm.SendCommand.ExecuteAsync(null);
        var saved = Assert.Single(Store.Load(Scope));
        var staging = new OutgoingStaging(Path.Combine(Paths.DataDirectory, "outgoing"));
        var path = staging.FilePath(Scope, saved.Id, Assert.Single(saved.Files));
        Assert.True(File.Exists(path));
        var other = Scope with { UserId = 2 };
        Store.Save(other, saved);
        var otherPath = staging.FilePath(other, saved.Id, saved.Files[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(otherPath)!);
        File.WriteAllText(otherPath, "other account");

        service.CurrentUserId = null;
        var cleanup = vm.ClearOutgoingForLogoutAsync();
        vm.Reset();
        Assert.True(await cleanup);

        Assert.Empty(Store.Load(Scope));
        Assert.False(File.Exists(path));
        Assert.Single(Store.Load(other));
        Assert.True(File.Exists(otherPath));
    }

    [Fact]
    public async Task CorruptRowIsRemovedWithItsStagingAndOtherChatsKeepSending()
    {
        await InitializeAsync();
        var broken = new OutgoingMessage(Guid.NewGuid(), "broken", "", 0, [], [], [], []);
        var valid = broken with { Id = Guid.NewGuid(), ChatId = "valid" };
        Store.Save(Scope, broken);
        Store.Save(Scope, valid);
        var staged = new OutgoingStaging(Path.Combine(Paths.DataDirectory, "outgoing"));
        var brokenPath = staged.FilePath(Scope, broken.Id, new(Guid.NewGuid(), "file", null, default, ".bin"));
        Directory.CreateDirectory(Path.GetDirectoryName(brokenPath)!);
        File.WriteAllText(brokenPath, "broken");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE outgoing_messages SET protected_data = X'00' WHERE operation_id = $id";
            command.Parameters.AddWithValue("$id", broken.Id.ToString());
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var queue = Start(new Transport(send: (message, _) =>
            Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), Sent(message)))), start: false);
        var delivered = Delivery(queue);
        await queue.StartAsync(Scope);
        Assert.Equal(valid.Id.ToString(), (await delivered.Task.WaitAsync(Timeout)).ClientOperationId);
        Assert.Empty(Store.Load(Scope));
        Assert.False(File.Exists(brokenPath));
    }

    [Fact]
    public async Task StartupRemovesOrphanStagingAcrossAccounts()
    {
        await InitializeAsync();
        var other = Scope with { UserId = 42 };
        var saved = new OutgoingMessage(Guid.NewGuid(), "a", "", 0, [], [], [], []);
        Store.Save(other, saved);
        var staging = new OutgoingStaging(Path.Combine(Paths.DataDirectory, "outgoing"));
        var file = new OutgoingFile(Guid.NewGuid(), "file", null, default, ".bin");
        var kept = staging.FilePath(other, saved.Id, file);
        var orphan = staging.FilePath(other, Guid.NewGuid(), file);
        foreach (var path in new[] { kept, orphan })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "file");
        }

        await Start(new Transport(), start: false).SweepOrphansAsync();
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public async Task ConfirmDoesNotRemoveStorageOnCallingThread()
    {
        await InitializeAsync();
        var file = new OutgoingFile(Guid.NewGuid(), "file", null, default, ".bin");
        var message = new OutgoingMessage(Guid.NewGuid(), "a", "", 0, [], [], [file], ["uploaded"]);
        var staging = new OutgoingStaging(Path.Combine(Paths.DataDirectory, "outgoing"));
        var path = staging.FilePath(Scope, message.Id, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "staged");
        Store.Save(Scope, message);
        var store = new RemovalThreadStore(Store) { BeforeRemove = () => Assert.True(File.Exists(path)) };
        var queue = new OutgoingMessageQueue(store, new Transport(send: (_, token) => NeverSendAsync(token)), Paths, _clock);
        _queues.Add(queue);
        await queue.StartAsync(Scope);
        var confirmation = new TaskCompletionSource<(int Caller, bool Confirmed)>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try
            {
                var caller = Environment.CurrentManagedThreadId;
                confirmation.SetResult((caller, queue.ConfirmAsync(Scope, "a", Sent(message)).GetAwaiter().GetResult()));
            }
            catch (Exception ex) { confirmation.SetException(ex); }
        }).Start();
        var result = await confirmation.Task.WaitAsync(Timeout);
        Assert.True(result.Confirmed);
        Assert.NotEqual(result.Caller, store.RemovalThread);
        Assert.False(File.Exists(path));
    }

    private sealed class RemovalThreadStore(IOutgoingMessageStore inner) : IOutgoingMessageStore
    {
        public int RemovalThread { get; private set; }
        public bool FailLoad { get; init; }
        public Action? BeforeRemove { get; init; }
        public IReadOnlyList<OutgoingMessage> Load(OutgoingScope scope) => FailLoad
            ? throw new Microsoft.Data.Sqlite.SqliteException("Database unavailable", 5)
            : inner.Load(scope);
        public IReadOnlyList<(OutgoingScope Scope, Guid Id)> ListOperations() => inner.ListOperations();
        public void Save(OutgoingScope scope, OutgoingMessage message) => inner.Save(scope, message);
        public bool TrySaveNotificationReply(OutgoingScope scope, OutgoingMessage message) =>
            inner.TrySaveNotificationReply(scope, message);
        public void Clear(OutgoingScope scope) => inner.Clear(scope);
        public void Remove(OutgoingScope scope, Guid id)
        {
            RemovalThread = Environment.CurrentManagedThreadId;
            BeforeRemove?.Invoke();
            inner.Remove(scope, id);
        }
    }

    public void Dispose()
    {
        foreach (var queue in _queues)
            queue.StopAsync().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(0)] // Слот ещё не запрошен.
    [InlineData(1)] // Слот создан, ответ загрузки потерян.
    [InlineData(2)] // ID вложения записан, отправка ещё не принята.
    [InlineData(3)] // Сервер принял сообщение, ответ потерян.
    public async Task RecoversEachStageWithStableOperationIds(int stage)
    {
        await InitializeAsync();
        var reached = Signal();
        var stopped = Signal();
        var uploaded = new ConcurrentDictionary<Guid, string>();
        var accepted = new ConcurrentDictionary<Guid, MessageModel>();
        var uploadCalls = new ConcurrentQueue<Guid>();
        var sendCalls = new ConcurrentQueue<Guid>();
        var transport = new Transport(async (file, path, token) =>
        {
            uploadCalls.Enqueue(file.Id);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path, token));
            if (stage == 0)
                await BlockAsync(token);
            var id = uploaded.GetOrAdd(file.Id, "server-file");
            if (stage == 1)
                await BlockAsync(token);
            return (new(true), id);
        }, async (message, token) =>
        {
            sendCalls.Enqueue(message.Id);
            if (stage == 3)
                accepted.GetOrAdd(message.Id, _ => Sent(message));
            await BlockAsync(token);
            return (new(true), Sent(message));
        });

        var first = Start(transport);
        var source = Path.Combine(_root, "draft.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await first.EnqueueAsync(Scope, "a", "caption", 7, null, null,
            [(new(source, "draft.bin"), UploadFileType.MessageAttachmentDocument)], default);
        File.Delete(source);
        await reached.Task.WaitAsync(Timeout);
        var saved = Assert.Single(Store.Load(Scope));
        Assert.Equal(stage >= 2 ? ["server-file"] : Array.Empty<string>(), saved.Uploaded);
        await first.StopAsync();
        await stopped.Task.WaitAsync(Timeout);

        var restarted = Start(new Transport(async (file, path, token) =>
        {
            uploadCalls.Enqueue(file.Id);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path, token));
            return (new(true), uploaded.GetOrAdd(file.Id, "server-file"));
        }, (message, _) =>
        {
            sendCalls.Enqueue(message.Id);
            Assert.Equal(new[] { "server-file" }, message.Uploaded);
            Assert.Equal("caption", message.Text);
            Assert.Equal(7, message.ReplyId);
            return Task.FromResult<(ErrorReturner, MessageModel?)>((new(true),
                accepted.GetOrAdd(message.Id, _ => Sent(message))));
        }), start: false);
        var delivered = Delivery(restarted);
        await restarted.StartAsync(Scope);
        Assert.Equal(saved.Id.ToString(), (await delivered.Task.WaitAsync(Timeout)).ClientOperationId);
        Assert.Single(accepted);
        Assert.Single(uploaded);
        Assert.All(sendCalls, id => Assert.Equal(saved.Id, id));
        Assert.All(uploadCalls, id => Assert.Equal(saved.Files[0].Id, id));
        Assert.Equal(stage < 2 ? 2 : 1, uploadCalls.Count);
        Assert.Equal(stage < 2 ? 1 : 2, sendCalls.Count);
        Assert.Empty(Store.Load(Scope));
        Assert.Empty(Directory.EnumerateFiles(Paths.DataDirectory, "*.bin", SearchOption.AllDirectories));

        async Task BlockAsync(CancellationToken token)
        {
            reached.TrySetResult();
            try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); }
            finally { stopped.TrySetResult(); }
        }
    }

    [Fact]
    public async Task BackoffAndFailureBlockOnlyTheirChatUntilRetryOrCancel()
    {
        await InitializeAsync();
        var calls = new ConcurrentQueue<string>();
        var firstAttempts = 0;
        var queue = Start(new Transport(send: (message, _) =>
        {
            calls.Enqueue(message.Text);
            if (message.Text == "first")
            {
                var attempt = Interlocked.Increment(ref firstAttempts);
                if (attempt <= 2)
                    return Task.FromResult<(ErrorReturner, MessageModel?)>((attempt == 1 ? new(false)
                        : new(false, errorResourceKey: "Messenger_UserUnavailable"), null));
            }
            if (message.Text == "cancel")
                return Task.FromResult<(ErrorReturner, MessageModel?)>((new(false,
                    errorResourceKey: "Messenger_AttachmentReadFailed"), null));
            return Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), Sent(message)));
        }));
        var firstWaiting = WaitForAsync(queue, () => firstAttempts == 1 &&
            queue.Snapshot(Scope, "a").FirstOrDefault()?.State == OutgoingState.Waiting);
        await EnqueueAsync(queue, "a", "first");
        await firstWaiting;
        await EnqueueAsync(queue, "a", "cancel");
        await EnqueueAsync(queue, "a", "last");
        var other = Delivery(queue, "b");
        await EnqueueAsync(queue, "b", "other");
        await other.Task.WaitAsync(Timeout);
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(new[] { "first", "other" }, calls);
        var blockedFirst = WaitForAsync(queue, () => queue.Snapshot(Scope, "a").FirstOrDefault()?.State == OutgoingState.Failed);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await blockedFirst;
        var first = queue.Snapshot(Scope, "a")[0];
        Assert.Equal("Messenger_UserUnavailable", first.Message.Error);
        Assert.Equal(new[] { "first", "other", "first" }, calls);

        var blockedSecond = WaitForAsync(queue, () =>
            queue.Snapshot(Scope, "a").FirstOrDefault()?.Message.Error == "Messenger_AttachmentReadFailed");
        await queue.RetryAsync(Scope, first.Message.Id);
        await blockedSecond;
        var last = Delivery(queue, "a");
        await queue.CancelAsync(Scope, queue.Snapshot(Scope, "a")[0].Message.Id);
        Assert.Equal("last", (await last.Task.WaitAsync(Timeout)).Text);
        Assert.Equal(new[] { "first", "other", "first", "first", "cancel", "last" }, calls);
    }

    [Fact]
    public async Task ScopesAreIsolatedPayloadIsProtectedAndLogoutSweepsOwnedStaging()
    {
        await InitializeAsync();
        var queue = Start(new Transport(async (_, _, token) =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return default;
        }));
        const string body = "protected-body-18c5e";
        var source = Path.Combine(_root, "draft.bin");
        await File.WriteAllBytesAsync(source, [1]);
        await queue.EnqueueAsync(Scope, "a", body, 0, null, null,
            [(new(source, "draft.bin"), UploadFileType.MessageAttachmentDocument)], default);
        var staged = Assert.Single(Directory.EnumerateFiles(Paths.DataDirectory, "*.bin", SearchOption.AllDirectories));
        var operationDirectory = Directory.GetParent(staged)!;
        Assert.True(operationDirectory.GetAccessControl().AreAccessRulesProtected);
        var orphan = Directory.CreateDirectory(Path.Combine(operationDirectory.Parent!.FullName, Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(orphan.FullName, "orphan"), "orphan");
        var otherNode = OutgoingScope.Create("https://other.invalid", Scope.UserId);
        var otherUser = Scope with { UserId = 2 };
        Assert.Empty(Store.Load(otherNode));
        Assert.Empty(Store.Load(otherUser));
        Assert.Empty(queue.Snapshot(otherNode, "a"));
        Assert.Empty(queue.Snapshot(otherUser, "a"));

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_data FROM outgoing_messages";
        var blob = Assert.IsType<byte[]>(command.ExecuteScalar());
        Assert.DoesNotContain(body, System.Text.Encoding.UTF8.GetString(blob));

        await queue.StartAsync(otherUser);
        Assert.Empty(queue.Snapshot(Scope, "a"));
        Assert.True(File.Exists(staged));
        await queue.StartAsync(Scope);
        Assert.False(Directory.Exists(orphan.FullName));
        await queue.ClearAsync(Scope);
        Assert.Empty(Store.Load(Scope));
        Assert.False(File.Exists(staged));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseAndRealtimeEchoProduceOnlyOneBubble(bool echoFirst)
    {
        await InitializeAsync();
        var request = new TaskCompletionSource<OutgoingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<(ErrorReturner, MessageModel?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = Start(new Transport(send: (message, _) =>
        {
            request.TrySetResult(message);
            return response.Task;
        }), start: false);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        var dispatcher = new Dispatcher();
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: dispatcher, outgoing: queue);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        vm.DraftText = "offline text";
        await vm.SendCommand.ExecuteAsync(null);
        var outgoing = await request.Task.WaitAsync(Timeout);
        await dispatcher.RunUntilAsync(() => vm.PendingMessages.Count == 1);
        Assert.Empty(vm.DraftText);
        Assert.Single(vm.PendingMessages);
        Assert.Empty(vm.Messages);

        var sent = Sent(outgoing);
        if (echoFirst)
        {
            realtime.Raise(new IncomingMessage("a", sent));
            await dispatcher.RunUntilAsync(() => vm.Messages.Count == 1 && vm.PendingMessages.Count == 0);
            Assert.Empty(vm.PendingMessages);
            Assert.Single(vm.Messages);
        }

        response.SetResult((new(true), sent));
        await dispatcher.RunUntilAsync(() => vm.Messages.Count == 1 && vm.PendingMessages.Count == 0);
        realtime.Raise(new IncomingMessage("a", sent));
        dispatcher.Drain();
        Assert.Empty(vm.PendingMessages);
        Assert.Equal(sent.MessageId, Assert.Single(vm.Messages).Id);
        Assert.Empty(Store.Load(Scope));
        vm.Reset();
    }

    [Fact]
    public async Task RealtimeEditAfterEchoAppliesInArrivalOrder()
    {
        await InitializeAsync();
        var request = new TaskCompletionSource<OutgoingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = Start(new Transport(send: (message, token) =>
        {
            request.SetResult(message);
            return NeverSendAsync(token);
        }), start: false);
        var realtime = new FakeRealtimeMessengerService();
        var messenger = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        var dispatcher = new Dispatcher();
        var vm = MessengerTestDoubles.CreateViewModel(messenger, realtime, dispatcher: dispatcher, outgoing: queue);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        vm.DraftText = "original";
        await vm.SendCommand.ExecuteAsync(null);
        var outgoing = await request.Task.WaitAsync(Timeout);
        await dispatcher.RunUntilAsync(() => vm.PendingMessages.Count == 1);

        var inserted = new List<string>();
        ((System.Collections.Specialized.INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                inserted.Add(vm.Messages[^1].Text);
        };
        var sent = Sent(outgoing);
        var edited = Sent(outgoing with { Text = "edited" });
        edited.IsEdited = true;
        edited.EditedAt = new() { Seconds = 100 };
        realtime.Raise(new IncomingMessage("a", sent));
        realtime.Raise(new MessageUpdate("a", false, sent.MessageId, MessageUpdateKind.Edit, Message: edited));
        await dispatcher.RunUntilAsync(() => vm.Messages.Count == 1 && vm.PendingMessages.Count == 0);

        Assert.Equal("original", Assert.Single(inserted));
        Assert.Equal("edited", Assert.Single(vm.Messages).Text);
        Assert.Equal("edited", vm.SelectedChat.Preview);
        Assert.Empty(Store.Load(Scope));
        vm.Reset();
    }

    [Fact]
    public async Task ComposerRoutesPersistTextReplyForwardAttachmentAndVoiceBeforeDelivery()
    {
        await InitializeAsync();
        var queue = Start(new Transport(send: (_, token) => NeverSendAsync(token)), start: false);
        var service = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        service.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        service.Messages.Add(MessengerTestDoubles.CreateMessage(7, "a", 2));
        var dispatcher = new Dispatcher();
        var vm = MessengerTestDoubles.CreateViewModel(service, dispatcher: dispatcher, outgoing: queue,
            voiceRecorder: new MessengerVoiceTests.Recorder());
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        dispatcher.Drain();
        var original = Assert.Single(vm.Messages);

        vm.DraftText = "text";
        await vm.SendCommand.ExecuteAsync(null);
        vm.StartReplyMessageCommand.Execute(original);
        vm.DraftText = "reply";
        await vm.SendCommand.ExecuteAsync(null);
        vm.StartForwardMessageCommand.Execute(original);
        vm.ToggleForwardTargetCommand.Execute(Assert.Single(vm.ForwardTargets));
        vm.ForwardComment = "forward";
        await vm.SubmitForwardCommand.ExecuteAsync(null);

        var path = Path.Combine(_root, "from-composer.bin");
        await File.WriteAllBytesAsync(path, [9]);
        await vm.AddAttachmentsAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>(
            [new(path, "file.bin", IsTemporary: true)]));
        vm.DraftText = "attachment";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(vm.AttachmentComposer.Files);
        Assert.False(File.Exists(path));

        var recording = vm.RecordVoiceCommand.ExecuteAsync(null);
        vm.StopVoiceRecordingCommand.Execute(null);
        await recording;
        Assert.Single(vm.AttachmentComposer.Files);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(vm.AttachmentComposer.Files);

        var saved = Store.Load(Scope);
        Assert.Equal(new[] { "text", "reply", "forward", "attachment", "" }, saved.Select(message => message.Text));
        Assert.Equal(7, saved[1].ReplyId);
        Assert.Equal(new long[] { 7 }, saved[2].Forwards);
        Assert.Equal(UploadFileType.MessageAttachmentDocument, Assert.Single(saved[3].Files).Type);
        Assert.Equal(UploadFileType.MessageAttachmentVoice, Assert.Single(saved[4].Files).Type);
        dispatcher.Drain();
        Assert.Equal(5, vm.PendingMessages.Count);
        vm.Reset();

        var restarted = Start(new Transport(send: (_, token) => NeverSendAsync(token)), start: false);
        var restored = MessengerTestDoubles.CreateViewModel(service, dispatcher: dispatcher, outgoing: restarted);
        await restored.LoadAsync();
        restored.SelectedChat = Assert.Single(restored.Chats);
        await dispatcher.RunUntilAsync(() => restored.PendingMessages.Count == 5);
        Assert.Equal(5, restored.PendingMessages.Count);
        restored.Reset();
    }

    [Fact]
    public async Task MissingSourceBeforeCommitKeepsComposerAndDraft()
    {
        await InitializeAsync();
        var queue = Start(new Transport(send: (_, token) => NeverSendAsync(token)), start: false);
        var service = new FakeMessengerService { CurrentNodeAddress = Scope.Node };
        service.Chats.Add(MessengerTestDoubles.CreateChat("a"));
        var vm = MessengerTestDoubles.CreateViewModel(service, outgoing: queue);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        vm.DraftText = "keep me";

        var file = Path.Combine(_root, "vanishing.bin");
        await File.WriteAllBytesAsync(file, [1]);
        await vm.AddAttachmentsAsync(_ => Task.FromResult<IReadOnlyList<AttachmentSourceFile>>([new(file, "file.bin")]));
        File.Delete(file);
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("keep me", vm.DraftText);
        Assert.Single(vm.AttachmentComposer.Files);
        Assert.Empty(vm.PendingMessages);
        Assert.Empty(Store.Load(Scope));
        Assert.Equal("Messenger_OutboxStorageFailed", vm.ActionError);
        vm.Reset();
    }

    [Fact]
    public async Task StickerQueuesFileAndReplyWhileKeepingTextDraft()
    {
        await InitializeAsync();
        var queue = Start(new Transport(send: (_, token) => NeverSendAsync(token)), start: false);
        var (vm, _, sticker) = await MessengerStickerTests.OpenMessengerAsync(queue, Scope.Node);
        vm.StartReplyMessageCommand.Execute(Assert.Single(vm.Messages));
        vm.DraftText = "keep";
        Assert.True(await vm.SendStickerAsync(sticker));
        var message = Assert.Single(Store.Load(Scope));
        Assert.Equal(new[] { sticker.FileId }, message.Attachments);
        Assert.Equal(50, message.ReplyId);
        Assert.Equal("keep", vm.DraftText);
        Assert.Null(vm.ReplyTarget);
        vm.Reset();
    }

    private static async Task<(ErrorReturner, MessageModel?)> NeverSendAsync(CancellationToken token)
    {
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
        return default;
    }

    private Task InitializeAsync() => new SqliteApplicationDataStore(Paths).InitializeAsync();

    private OutgoingMessageQueue Start(Transport transport, bool start = true)
    {
        var queue = new OutgoingMessageQueue(Store, transport, Paths, _clock);
        _queues.Add(queue);
        if (start)
            queue.StartAsync(Scope).GetAwaiter().GetResult();
        return queue;
    }

    private static Task EnqueueAsync(OutgoingMessageQueue queue, string chat, string text) =>
        queue.EnqueueAsync(Scope, chat, text, 0, null, null, [], default);

    private static MessageModel Sent(OutgoingMessage message)
    {
        var sent = MessengerTestDoubles.CreateMessage(50, message.ChatId, Scope.UserId, message.Text);
        sent.ClientOperationId = message.Id.ToString();
        return sent;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<MessageModel> Delivery(OutgoingMessageQueue queue, string? chat = null)
    {
        var signal = new TaskCompletionSource<MessageModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Delivered += (_, message) =>
        {
            if (chat is null || message.ChatId == chat)
                signal.TrySetResult(message);
        };
        return signal;
    }

    private static async Task WaitForAsync(OutgoingMessageQueue queue, Func<bool> predicate)
    {
        var signal = Signal();
        void Changed()
        {
            if (predicate())
                signal.TrySetResult();
        }
        queue.Changed += Changed;
        try
        {
            Changed();
            await signal.Task.WaitAsync(Timeout);
        }
        finally { queue.Changed -= Changed; }
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        private readonly Channel<Action> _actions = Channel.CreateUnbounded<Action>();
        public void Post(Action action) => _actions.Writer.TryWrite(action);

        public void Drain()
        {
            while (_actions.Reader.TryRead(out var action))
                action();
        }

        public async Task RunUntilAsync(Func<bool> condition)
        {
            using var cancellation = new CancellationTokenSource(Timeout);
            while (!condition())
                (await _actions.Reader.ReadAsync(cancellation.Token))();
        }
    }

    private sealed class Transport(
        Func<OutgoingFile, string, CancellationToken, Task<(ErrorReturner, string?)>>? upload = null,
        Func<OutgoingMessage, CancellationToken, Task<(ErrorReturner, MessageModel?)>>? send = null)
        : IOutgoingMessageTransport
    {
        public Task<long?> GetStorageAvailableAsync(OutgoingScope scope, CancellationToken token) =>
            Task.FromResult<long?>(null);

        public Task<(ErrorReturner error, string? fileId)> UploadAsync(OutgoingScope scope, OutgoingFile file,
            string path, IProgress<double> progress, CancellationToken token) => upload!(file, path, token);

        public Task<(ErrorReturner error, MessageModel? message)> SendAsync(OutgoingScope scope,
            OutgoingMessage message, CancellationToken token) => send!(message, token);
    }
}

