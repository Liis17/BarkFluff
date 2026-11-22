using BarkFluff.Client.Core.Infrastructure.Realtime;
using BarkFluff.Proto.Users;
using System.Text;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.Crypto;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerResyncTests
{
    private static readonly Timestamp Start = Timestamp.FromDateTime(DateTime.UnixEpoch);

    [Fact]
    public async Task RegularUpdates_ApplyToMatchingChatAndKeepQuotesAndActionsConsistent()
    {
        var (vm, api, updates) = await OpenAsync((_, _) => Task.CompletedTask);
        var original = vm.Messages[0];
        var quote = vm.Messages[1];
        vm.StartReplyMessageCommand.Execute(original);
        vm.RequestDeleteMessageCommand.Execute(original);

        var edit = MakeMessage(1, "edited", edited: true);
        edit.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Document, FileName = "new.txt" });
        updates.Raise(new MessageUpdate("other", false, 1, MessageUpdateKind.Edit, Message: edit));
        updates.Raise(new MessageUpdate("a", true, 1, MessageUpdateKind.Delete));
        Assert.Equal("before", original.Text);

        var update = new MessageUpdate("a", false, 1, MessageUpdateKind.Edit, Message: edit);
        updates.Raise(update);
        updates.Raise(update);
        Assert.Same(original, vm.Messages[0]);
        Assert.True(original.IsEdited);
        Assert.Equal("new.txt", Assert.Single(original.FileAttachments).FileName);
        Assert.Equal("edited", quote.Reply?.Preview);
        Assert.Equal("edited", vm.SelectedChat!.Preview);

        var pin = new MessageUpdate("a", false, 1, MessageUpdateKind.Pin, PinnedAt: Start);
        updates.Raise(pin);
        var bar = vm.PinnedPreview;
        updates.Raise(pin);
        Assert.Same(bar, vm.PinnedPreview);
        Assert.True(original.IsPinned);
        updates.Raise(pin with { Kind = MessageUpdateKind.Unpin });
        Assert.False(original.IsPinned);
        updates.Raise(pin);
        updates.Raise(pin with { Kind = MessageUpdateKind.UnpinAll });
        Assert.Null(vm.PinnedPreview);
        Assert.False(original.IsPinned);

        api.Messages.RemoveAt(0);
        api.Chats[0].LastMessage = new Message { Id = 2, Content = new MessageContent { Text = "reply" } };
        api.Chats[0].CountUnread = 0;
        updates.Raise(pin with { Kind = MessageUpdateKind.Delete });
        updates.Raise(pin with { Kind = MessageUpdateKind.Delete });
        Assert.Same(quote, Assert.Single(vm.Messages));
        Assert.True(quote.Reply?.IsDeleted);
        Assert.Null(vm.ReplyTarget);
        Assert.Null(vm.MessagePendingDelete);
        Assert.Equal("reply", vm.SelectedChat.Preview);
        Assert.Equal(0, vm.SelectedChat.UnreadCount);
        vm.Reset();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrivateUpdates_DecryptOnlyWithLocalKey_AndDeduplicateUnread(bool unlocked)
    {
        var key = new byte[32];
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("a", chatType: ChatType.Private);
        chat.PrivateInviteState = PrivateChatInviteState.Accepted;
        chat.LastActivityAt = Start;
        api.Chats.Add(chat);
        var keys = new FakePrivateChatKeyStore { Key = unlocked ? key : null };
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates, keys: keys);
        vm.ResyncDelay = (_, _) => Task.CompletedTask;
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        updates.Raise(new IncomingMessage("a", MakeMessage(7, "regular")));
        var created = new MessageUpdate("a", true, 7, MessageUpdateKind.New, Encrypted: Encrypt(7, "secret", key));
        updates.Raise(created);
        updates.Raise(created);
        Assert.Equal(1, vm.SelectedChat.UnreadCount);
        Assert.Empty(vm.SelectedChat.Preview);
        if (unlocked)
        {
            var item = Assert.Single(vm.Messages);
            Assert.Equal("secret", item.Text);
            updates.Raise(created with { Kind = MessageUpdateKind.Edit, Encrypted = Encrypt(7, "changed", key, edited: true) });
            Assert.Equal("changed", item.Text);
            Assert.True(item.IsEdited);
        }
        else
        {
            Assert.Empty(vm.Messages);
        }

        updates.Raise(created with { Kind = MessageUpdateKind.Delete, Encrypted = null });
        updates.Raise(created);
        Assert.Empty(vm.Messages);
        Assert.Empty(vm.SelectedChat.Preview);
        Assert.Equal(0, vm.SelectedChat.UnreadCount);
        vm.Reset();
    }

    [Fact]
    public async Task LateHistoryAndAttachmentResponses_CannotReplaceEditsOrResurrectDeletes()
    {
        var api = CreateApi();
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        await vm.LoadAsync();
        var history = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        api.History = (_, _) => history.Task;
        vm.SelectedChat = vm.Chats.Single();

        var file = new TaskCompletionSource<string>();
        api.FileUrl = _ => file.Task;
        var incoming = MakeMessage(3, "stale incoming");
        incoming.Attachments.Add(new AttachmentsModel { Type = MessageAttachmentType.Document, FileId = "file" });
        updates.Raise(new IncomingMessage("a", incoming));
        updates.Raise(new MessageUpdate("a", false, 3, MessageUpdateKind.Edit, Message: MakeMessage(3, "latest incoming", edited: true)));
        updates.Raise(new MessageUpdate("a", false, 1, MessageUpdateKind.Edit, Message: MakeMessage(1, "latest history", edited: true)));
        updates.Raise(new MessageUpdate("a", false, 2, MessageUpdateKind.Delete));
        api.History = null;
        api.Messages.Clear();
        api.Messages.Add(MakeMessage(1, "latest history", edited: true));
        api.Messages.Add(MakeMessage(3, "latest incoming", edited: true));

        var initialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.ScrollRequest) && vm.ScrollRequest is not null)
            {
                initialLoad.TrySetResult();
            }
        };
        file.SetResult("resolved");
        history.SetResult((new(true), [MakeMessage(1, "stale history"), MakeMessage(2, "deleted")]));
        await initialLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["latest history", "latest incoming"], vm.Messages.Select(message => message.Text));
        vm.Reset();
    }

    [Fact]
    public async Task ReconnectAndStreamReopen_ReconcileDeletedEdgesEditsNewMessagesAndPins()
    {
        var (vm, api, updates) = await OpenAsync();
        var selected = vm.SelectedChat;
        var surviving = vm.Messages[1];
        vm.DraftText = "draft";
        vm.FeedPositionChangedCommand.Execute(false);
        vm.ScrollRequest = null;
        updates.RaiseConnection(false);
        api.Messages.Clear();
        api.Messages.Add(MakeMessage(2, "catchup edit", edited: true));
        api.Messages.Add(MakeMessage(3, "catchup new"));
        api.Pins.Add(new() { Message = new Message { Id = 2, Content = new MessageContent { Text = "catchup edit" } } });
        updates.Resync();
        Assert.Equal("reply", surviving.Text);
        updates.RaiseConnection(true);
        Assert.Same(surviving, vm.Messages[0]);
        Assert.Equal(["catchup edit", "catchup new"], vm.Messages.Select(item => item.Text));
        Assert.True(surviving.IsPinned);
        Assert.Same(selected, vm.SelectedChat);
        Assert.Equal("draft", vm.DraftText);
        Assert.Null(vm.ScrollRequest);

        api.History = (_, _) => Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(false, errorCode: 1), null));
        api.Pins.Clear();
        api.Chats.Clear(); // Empty regular chats are absent from the first page.
        updates.Resync();
        Assert.Empty(vm.Messages);
        Assert.Null(vm.PinnedPreview);
        Assert.Same(selected, Assert.Single(vm.VisibleChats));
        Assert.Empty(selected!.Preview);
        vm.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resync_ChecksLoadedAnchorBeforeInferringDeletionAtWindowEdge(bool secondDeleted)
    {
        var api = CreateApi();
        if (secondDeleted)
        {
            var edge = MakeMessage(3, "deleted edge");
            edge.SentAt = new Timestamp { Nanos = 2 };
            api.Messages.Add(edge);
        }
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        var surviving = vm.Messages.Single(message => message.Id == 2);
        var latest = Enumerable.Range(secondDeleted ? 4 : 3, 50).Select(id => MakeMessage(id, $"new {id}")).ToList();
        (ErrorReturner Error, List<MessageModel>? Rows) firstPage = (new(true), latest);
        var repaired = MakeMessage(2, "revised", edited: true);
        repaired.SentAt = api.Messages[1].SentAt;
        (ErrorReturner Error, List<MessageModel>? Rows) secondPage = (new(true), [repaired, .. latest]);
        var requested = new List<long>();
        api.History = (_, anchor) =>
        {
            requested.Add(anchor);
            return Task.FromResult(anchor switch
            {
                0 => firstPage,
                2 => secondPage,
                _ => (new ErrorReturner(false), null)
            });
        };

        updates.Resync();

        Assert.Equal(secondDeleted ? [0L, 1L, 3L, 2L] : [0L, 1L, 2L], requested);
        Assert.Same(surviving, vm.Messages[0]);
        Assert.Equal("revised", surviving.Text);
        Assert.Equal(51, vm.Messages.Count);
        Assert.DoesNotContain(vm.Messages, message => message.Id == 1);
        if (secondDeleted)
        {
            Assert.DoesNotContain(vm.Messages, message => message.Id == 3);
        }
        vm.Reset();
    }

    [Fact]
    public async Task ReopenConfirmation_RepairsPinsWithoutApplyingOlderPinSnapshot()
    {
        var api = CreateApi();
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        var wait = new TaskCompletionSource();
        vm.ResyncDelay = (_, _) => wait.Task;
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        var oldPins = new TaskCompletionSource<(ErrorReturner, List<PinnedMessageInfo>?)>();
        api.PinnedHistory = () => oldPins.Task;
        updates.Resync();
        updates.Raise(new MessageUpdate("a", false, 1, MessageUpdateKind.UnpinAll));
        oldPins.SetResult((new(true), [new() { Message = new Message { Id = 1 } }]));
        Assert.Null(vm.PinnedPreview);

        api.PinnedHistory = null;
        api.Pins.Add(new() { Message = new Message { Id = 2 } });
        updates.Resync(immediate: false);
        Assert.Null(vm.PinnedPreview);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pinned = vm.Messages[1];
        pinned.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(pinned.IsPinned) && pinned.IsPinned)
            {
                recovered.TrySetResult();
            }
        };
        wait.SetResult();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, vm.PinnedPreview?.MessageId);
        Assert.True(vm.Messages[1].IsPinned);
        vm.Reset();
    }

    [Fact]
    public async Task PrivateResync_MergesNewAndEditedRowsAndRemovesServerTombstones()
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("p", chatType: ChatType.Private);
        chat.PrivateInviteState = PrivateChatInviteState.Accepted;
        api.Chats.Add(chat);
        api.PrivateMessages.AddRange([
            new() { ChatId = "p", MessageId = 1, SentAt = new() { Seconds = 1 }, Text = "old" },
            new() { ChatId = "p", MessageId = 2, SentAt = new() { Seconds = 2 }, Text = "before" }
        ]);
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates, keys: new() { Key = new byte[32] });
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        api.PrivateMessages[0].IsDeleted = true;
        api.PrivateMessages[1].IsEdited = true;
        api.PrivateMessages[1].Text = "edited";
        api.PrivateMessages.Add(new() { ChatId = "p", MessageId = 3, SentAt = new() { Seconds = 3 }, Text = "new" });

        updates.Resync();

        Assert.Equal(["edited", "new"], vm.Messages.Select(item => item.Text));
        Assert.True(vm.Messages[0].IsEdited);
        Assert.Empty(vm.SelectedChat.Preview);
        vm.Reset();
    }

    [Fact]
    public async Task ResetAndAccountChange_IgnoreQueuedEventsAndOldSnapshots()
    {
        var api = CreateApi();
        var updates = new FakeRealtimeMessengerService();
        var dispatcher = new QueuedDispatcher();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates, dispatcher: dispatcher);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        var oldSnapshot = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        api.ChatSnapshot = () => oldSnapshot.Task;
        updates.Resync();
        dispatcher.Drain();

        updates.Raise(new IncomingMessage("a", MakeMessage(30, "old account")));
        vm.Reset();
        api.ChatSnapshot = null;
        api.CurrentUserId = 2;
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        dispatcher.Drain();
        oldSnapshot.SetResult((new(true), [MessengerTestDoubles.CreateChat("old")]));

        Assert.Equal("a", Assert.Single(vm.Chats).Id);
        Assert.DoesNotContain(vm.Messages, message => message.Id == 30);
        vm.Reset();
    }

    [Fact]
    public async Task Resync_DistantTailUsesWindowAnchorsWithoutFillingTheGap()
    {
        var (vm, api, updates) = await OpenAsync();
        var calls = new List<long>();
        var history = Enumerable.Range(1, 5000)
            .Select(id => MakeMessage(id, id == 1 ? "corrected" : $"row {id}", edited: id == 1)).ToArray();
        history[1].SentAt = new Timestamp { Nanos = 1 };
        var ordered = history.OrderBy(row => row.SentAt).ToArray();
        api.History = (_, anchor) =>
        {
            calls.Add(anchor);
            var index = Array.FindIndex(ordered, row => row.MessageId == anchor);
            var start = Math.Max(0, index - 50);
            var end = Math.Min(ordered.Length, index + 51);
            var rows = anchor == 0 ? ordered[^50..] : ordered[start..end];
            return Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(true), rows.ToList()));
        };

        var chatSnapshot = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = vm.Messages.Single(message => message.Id == 1);
        changed.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(changed.IsEdited))
            {
                corrected.TrySetResult();
            }
        };
        api.ChatSnapshot = () => chatSnapshot.Task;
        updates.Resync();
        updates.Raise(new IncomingMessage("a", MakeMessage(5000, "received during resync")));
        Assert.DoesNotContain(vm.Messages, message => message.Id == 5000);
        chatSnapshot.SetResult((new(true), api.Chats));
        await corrected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(calls.Count, 1, 3);
        Assert.Contains(calls, anchor => anchor is 1 or 2);
        Assert.Equal("corrected", vm.Messages.Single(message => message.Id == 1).Text);
        Assert.DoesNotContain(vm.Messages, message => message.Id > 52);
        updates.Raise(new IncomingMessage("a", MakeMessage(5001, "after the gap")));
        Assert.DoesNotContain(vm.Messages, message => message.Id == 5001);
        vm.Reset();
    }

    [Fact]
    public async Task Resync_UnchangedContentDoesNotResolveFilesOrNotifyBindings()
    {
        var api = CreateApi();
        api.Messages[0].Attachments.Add(new AttachmentsModel
        {
            Type = MessageAttachmentType.Document, FileId = "file", FileName = "document"
        });
        var files = 0;
        api.FileUrl = _ => { files++; return Task.FromResult("resolved"); };
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        var message = vm.Messages.Single(item => item.Id == 1);
        var notifications = new List<string?>();
        message.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        api.Messages[0].ReadBy.Add(1);

        updates.Resync();

        Assert.Equal(1, files);
        Assert.Equal([nameof(message.IsReadByCurrentUser)], notifications);
        vm.Reset();
    }

    [Fact]
    public async Task Deletes_CoalesceChatRefreshWithoutFetchingHistoryOrPins()
    {
        var delay = new TaskCompletionSource();
        var (vm, api, updates) = await OpenAsync((_, _) => delay.Task);
        var history = 0;
        var pins = 0;
        var chats = 0;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.History = (_, _) => { history++; return Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(true), [])); };
        api.PinnedHistory = () => { pins++; return Task.FromResult<(ErrorReturner, List<PinnedMessageInfo>?)>((new(true), [])); };
        api.ChatSnapshot = () =>
        {
            chats++;
            return Task.FromResult<(ErrorReturner, List<Chat>?)>((new(true), api.Chats));
        };
        vm.SelectedChat!.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SelectedChat.UnreadCount) && vm.SelectedChat.UnreadCount == 0)
            {
                refreshed.TrySetResult();
            }
        };
        api.Chats[0].CountUnread = 0;
        api.Chats[0].LastMessage = null;

        updates.Raise(new MessageUpdate("a", false, 1, MessageUpdateKind.Delete));
        updates.Raise(new MessageUpdate("a", false, 99, MessageUpdateKind.Delete));

        Assert.Equal(0, chats);
        delay.SetResult();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, history);
        Assert.Equal(0, pins);
        Assert.Equal(1, chats);
        vm.Reset();
    }

    [Fact]
    public async Task ReorderingVisibleChats_DoesNotMoveOrRemoveSelectedChat()
    {
        var (vm, api, updates) = await OpenAsync();
        var selected = vm.SelectedChat;
        var other = MessengerTestDoubles.CreateChat("b");
        other.LastActivityAt = new Timestamp { Seconds = 50 };
        api.Chats.Add(other);
        updates.Resync();
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        vm.VisibleChats.CollectionChanged += (_, args) => changes.Add(args);

        updates.Raise(new IncomingMessage("a", MakeMessage(100, "new")));

        Assert.Same(selected, vm.VisibleChats[0]);
        Assert.Same(selected, vm.SelectedChat);
        var incomingChanges = changes.Count;
        Assert.True(incomingChanges > 0);

        updates.Resync();

        Assert.Same(selected, vm.VisibleChats[1]);
        Assert.Same(selected, vm.SelectedChat);
        Assert.True(changes.Count > incomingChanges);
        Assert.All(changes, change =>
        {
            Assert.NotEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, change.Action);
            Assert.NotEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Move, change.Action);
            Assert.False(change.OldItems?.Contains(selected) == true);
        });
        vm.Reset();
    }

    [Fact]
    public async Task RefreshChats_StartsBothProfilesBeforeAwaitingEither()
    {
        var api = CreateApi();
        api.Chats.Add(MessengerTestDoubles.CreateChat("b", peerUserId: 2));
        api.Chats.Add(MessengerTestDoubles.CreateChat("c", peerUserId: 3));
        var requested = new List<long>();
        var first = new TaskCompletionSource<(ErrorReturner, UserData?)>();
        api.UserProfile = id =>
        {
            requested.Add(id);
            return id == 2 ? first.Task : Task.FromResult<(ErrorReturner, UserData?)>((new(true), null));
        };
        var vm = MessengerTestDoubles.CreateViewModel(api);
        var load = vm.LoadAsync();
        var profilesBeforeFirstCompletes = requested.ToArray();
        first.SetResult((new(true), null));
        await load;

        Assert.Equal([2L, 3L], profilesBeforeFirstCompletes);
        Assert.Equal(3, vm.Chats.Count);
        vm.Reset();
    }

    [Fact]
    public async Task Resync_RecoversFailedInitialLoadWithUnreadScroll()
    {
        var api = CreateApi();
        api.Chats[0].FirstUnreadMessageId = 1;
        api.MessagesFail = true;
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        Assert.Empty(vm.Messages);
        Assert.Null(vm.ScrollRequest);

        api.MessagesFail = false;
        updates.Resync();

        Assert.Equal(1, vm.ScrollRequest?.MessageId);
        Assert.Equal(2, vm.Messages.Count);
        vm.Reset();
    }

    [Fact]
    public async Task ReadDuringChatSnapshot_ProtectsUnreadButAllowsPreviewAndActivity()
    {
        var api = CreateApi();
        api.Messages[0].SenderId = 2;
        api.Chats[0].FirstUnreadMessageId = 1;
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        var snapshot = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        api.ChatSnapshot = () => snapshot.Task;
        api.History = (_, _) => Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(false), null));

        updates.Resync();
        updates.Raise(new MessageReadReceipt("a", 1, [1]));
        var updated = api.Chats[0].Clone();
        updated.LastMessage.Content.Text = "fresh preview";
        updated.LastActivityAt = new Timestamp { Seconds = 500 };
        var previewChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SelectedChat.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SelectedChat.LastMessageAt))
            {
                previewChanged.TrySetResult();
            }
        };
        snapshot.SetResult((new(true), [updated]));
        await previewChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, vm.SelectedChat.UnreadCount);
        Assert.Equal("fresh preview", vm.SelectedChat.Preview);
        Assert.Equal(updated.LastActivityAt.ToDateTimeOffset(), vm.SelectedChat.LastMessageAt);
        vm.Reset();
    }

    [Fact]
    public async Task OpeningStreamsDuringInitialHistory_ProducesOnlyImmediateAndConfirmation()
    {
        var api = CreateApi();
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        var confirmation = new TaskCompletionSource();
        vm.ResyncDelay = (_, _) => confirmation.Task;
        await vm.LoadAsync();
        var initial = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        api.History = (_, _) => initial.Task;
        vm.SelectedChat = vm.Chats.Single();
        api.History = null;
        var snapshots = 0;
        var twoSnapshots = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.ChatSnapshot = () =>
        {
            if (++snapshots == 2)
            {
                twoSnapshots.TrySetResult();
            }

            return Task.FromResult<(ErrorReturner, List<Chat>?)>((new(true), api.Chats));
        };

        for (var stream = 0; stream < 11; stream++)
        {
            updates.Resync(immediate: false);
        }
        updates.Resync();
        Assert.Equal(0, snapshots);
        confirmation.SetResult();
        initial.SetResult((new(true), api.Messages));
        await twoSnapshots.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, snapshots);
        vm.Reset();
    }

    [Fact]
    public void Journal_ReleasesClosedPayloadAndStillRejectsReplayedEvents()
    {
        var journal = new MessageUpdateJournal();
        var edit = new MessageUpdate("a", false, 1, MessageUpdateKind.Edit, Message: MakeMessage(1, "changed", edited: true));
        var updating = journal.ReadHistory("a", false);
        var applying = journal.Record(edit)!;
        journal.Release("a", false, applying);
        Assert.NotNull(applying.Payload);

        updating.Dispose();

        Assert.Null(applying.Payload);
        Assert.Null(journal.Record(edit));
        var created = edit with { Kind = MessageUpdateKind.New, Message = MakeMessage(1, "old") };
        Assert.Equal(MessageUpdateKind.Edit, journal.Record(created)?.Kind);
        Assert.Null(journal.Record(created));
        var deleted = edit with { Kind = MessageUpdateKind.Delete, Message = null };
        Assert.NotNull(journal.Record(deleted));
        Assert.Null(journal.Record(created));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resync_BoundsMissingAnchorRequestsAndRetriesOnlyOnNetworkFailure(bool networkFailure)
    {
        var api = CreateApi();
        api.Chats[0].FirstUnreadMessageId = 51;
        api.Messages.Clear();
        api.Messages.AddRange(Enumerable.Range(1, 100).Select(id => MakeMessage(id, "old")));
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        var retry = new TaskCompletionSource();
        var delays = new List<TimeSpan>();
        vm.ResyncDelay = (delay, token) =>
        {
            delays.Add(delay);
            return retry.Task.WaitAsync(token);
        };
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        Assert.Equal(100, vm.Messages.Count);

        var tail = Enumerable.Range(151, 50).Select(id => MakeMessage(id, "tail")).ToList();
        var bridge = Enumerable.Range(101, 51).Select(id => MakeMessage(id, "bridge")).ToList();
        var requests = new List<long>();
        var recovered = false;
        api.History = (_, anchor) =>
        {
            requests.Add(anchor);
            return Task.FromResult<(ErrorReturner, List<MessageModel>?)>(anchor switch
            {
                _ when recovered => (new(false, errorCode: 1), null),
                0 when !networkFailure || requests.Count == 1 => (new(true), tail),
                151 when !networkFailure => (new(true), bridge),
                _ => (new(false), null)
            });
        };

        try
        {
            updates.Resync();
            if (networkFailure)
            {
                Assert.InRange(requests.Count, 2, 5);
                Assert.Equal(StreamRetryLoop.Backoff(1), Assert.Single(delays));
                recovered = true;
                retry.SetResult();
                Assert.Empty(vm.Messages);
            }
            else
            {
                Assert.InRange(requests.Count, 2, 12);
                Assert.Empty(delays);
                Assert.Equal(100, vm.Messages.Count);
                var previousCount = requests.Count;
                updates.Resync();
                Assert.InRange(requests.Count - previousCount, 2, 12);
                Assert.Empty(delays);
            }
        }
        finally
        {
            vm.Reset();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forward_IgnoresDeleteBeforeResponseAndChatSwitchDuringAttachmentLoad(bool switchChat)
    {
        var (vm, api, updates) = await OpenAsync();
        var other = MessengerTestDoubles.CreateChat("b");
        api.Chats.Add(other);
        updates.Resync();
        var response = new TaskCompletionSource<(ErrorReturner, MessageModel?)>();
        var attachment = new TaskCompletionSource<string>();
        var resolving = new TaskCompletionSource();
        api.SendResponse = () => response.Task;
        api.FileUrl = _ =>
        {
            resolving.TrySetResult();
            return attachment.Task;
        };
        var forwarded = MakeMessage(3, "forwarded");
        if (switchChat)
        {
            forwarded.Attachments.Add(new() { Type = MessageAttachmentType.Document, FileId = "file" });
        }
        vm.StartForwardMessageCommand.Execute(vm.Messages[0]);
        vm.ToggleForwardTargetCommand.Execute(vm.ForwardTargets.Single(target => target.ChatId == "a"));
        var sending = vm.SubmitForwardCommand.ExecuteAsync(null);
        if (!switchChat)
        {
            updates.Raise(new MessageUpdate("a", false, 3, MessageUpdateKind.Delete));
        }
        response.SetResult((new(true), forwarded));
        if (switchChat)
        {
            await resolving.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
            vm.ScrollRequest = null;
            attachment.SetResult("resolved");
        }
        await sending;

        Assert.DoesNotContain(vm.Messages, message => message.Id == 3);
        if (switchChat)
        {
            Assert.Null(vm.ScrollRequest);
        }
        vm.Reset();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_UpdatesOriginalChatAfterSwitchAndSuppressesItsError(bool success)
    {
        var (vm, api, updates) = await OpenAsync((_, token) => Task.Delay(Timeout.Infinite, token));
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        updates.Resync();
        var original = vm.SelectedChat!;
        var response = new TaskCompletionSource<ErrorReturner>();
        api.DeleteResponse = () => response.Task;
        var last = vm.Messages.Single(message => message.Id == api.Chats[0].LastMessage.Id);
        vm.RequestDeleteMessageCommand.Execute(last);
        var deleting = vm.ConfirmDeleteMessageCommand.ExecuteAsync(null);
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        response.SetResult(new(success, success ? null : "delete failed"));
        await deleting;

        Assert.Null(vm.ActionError);
        Assert.Equal(success ? string.Empty : last.Text, original.Preview);
        vm.SelectedChat = original; // The server double still returns its old history.
        Assert.Equal(!success, vm.Messages.Any(message => message.Id == last.Id));
        vm.Reset();
    }

    [Theory]
    [InlineData("chats")]
    [InlineData("tail")]
    [InlineData("window")]
    public async Task Send_DuringResyncSurvivesHistoryAndLateEcho(string pausedRequest)
    {
        var (vm, api, updates) = await OpenAsync();
        var sent = MakeMessage(pausedRequest == "window" ? 54 : 3, "sent during resync");
        api.SendResponse = () => Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), sent));
        var chatSnapshot = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        var historySnapshot = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        var oldChats = api.Chats.Select(chat => chat.Clone()).ToList();
        var oldRows = api.Messages.ToList();
        var tail = Enumerable.Range(4, 50).Select(id => MakeMessage(id, "tail")).ToList();
        var paused = false;
        if (pausedRequest == "chats")
        {
            api.ChatSnapshot = () =>
            {
                paused = true;
                return chatSnapshot.Task;
            };
        }
        api.History = (_, anchor) =>
        {
            if (pausedRequest == "chats")
            {
                return Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(true), [.. oldRows, sent]));
            }
            if (pausedRequest == "window" && anchor == 0)
            {
                return Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(true), tail));
            }
            paused = true;
            return historySnapshot.Task;
        };

        updates.Resync();
        Assert.True(paused);
        vm.DraftText = sent.Text;
        await vm.SendCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Messages, message => message.Id == sent.MessageId);
        chatSnapshot.TrySetResult((new(true), oldChats));
        historySnapshot.TrySetResult((new(true), pausedRequest == "window" ? [.. oldRows, .. tail] : oldRows));

        Assert.Same(row, Assert.Single(vm.Messages, message => message.Id == sent.MessageId));
        updates.Raise(new IncomingMessage("a", sent));
        Assert.Same(row, Assert.Single(vm.Messages, message => message.Id == sent.MessageId));
        Assert.Equal(sent.Text, vm.SelectedChat!.Preview);
        vm.Reset();
    }

    [Fact]
    public async Task Send_EchoKeepsPreviewWhenOlderChatSnapshotCompletes()
    {
        var (vm, api, updates) = await OpenAsync();
        var snapshot = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        var oldChats = api.Chats.Select(chat => chat.Clone()).ToList();
        api.ChatSnapshot = () => snapshot.Task;
        var sent = MakeMessage(3, "sent during resync");
        api.SendResponse = () => Task.FromResult<(ErrorReturner, MessageModel?)>((new(true), sent));

        updates.Resync();
        vm.DraftText = sent.Text;
        await vm.SendCommand.ExecuteAsync(null);
        updates.Raise(new IncomingMessage("a", sent));
        Assert.Equal(sent.Text, vm.SelectedChat!.Preview);
        snapshot.SetResult((new(true), oldChats));

        Assert.Equal(sent.Text, vm.SelectedChat.Preview);
        vm.Reset();
    }

    private static async Task<(MessengerViewModel, FakeMessengerService, FakeRealtimeMessengerService)> OpenAsync(
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var api = CreateApi();
        var updates = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, updates);
        if (delay is not null)
        {
            vm.ResyncDelay = delay;
        }
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single();
        return (vm, api, updates);
    }

    private static FakeMessengerService CreateApi()
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("a");
        chat.LastMessage = new Message { Id = 1, Content = new MessageContent { Text = "before" } };
        chat.LastActivityAt = Start;
        chat.CountUnread = 1;
        api.Chats.Add(chat);
        api.Messages.Add(MakeMessage(1, "before"));
        var reply = MakeMessage(2, "reply");
        reply.SentAt = new Timestamp { Nanos = 1 };
        reply.ReplyTo = new ReplyPreviewModel { MessageId = 1, TextPreview = "before", SenderName = "Sender" };
        api.Messages.Add(reply);
        return api;
    }

    private static MessageModel MakeMessage(long id, string text, bool edited = false)
    {
        var message = MessengerTestDoubles.CreateMessage(id, "a", 1, text);
        message.SentAt = new Timestamp { Seconds = id };
        message.IsEdited = edited;
        message.EditedAt = edited ? new Timestamp { Seconds = 100 + id } : null;
        return message;
    }

    private static EncryptedMessage Encrypt(long id, string text, byte[] key, bool edited = false)
    {
        var (cipher, nonce) = PrivateChatCrypto.Encrypt(Encoding.UTF8.GetBytes(text), key, PrivateChatCrypto.PrivateChatAad("a"));
        return new EncryptedMessage
        {
            Id = id,
            ChatId = "a",
            SenderId = 2,
            SentAt = new Timestamp { Seconds = id },
            Ciphertext = ByteString.CopyFrom(cipher),
            Nonce = ByteString.CopyFrom(nonce),
            IsEdited = edited,
            EditedAt = edited ? new Timestamp { Seconds = 100 + id } : null
        };
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queued = new();

        public void Post(Action action) => _queued.Enqueue(action);

        internal void Drain()
        {
            while (_queued.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
