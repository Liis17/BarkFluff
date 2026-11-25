using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerHistoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JumpDownUsesUnseenUnreadAnchorThenLoadsTrueTail(bool unreadWasShown)
    {
        var (api, chat, updates) = CreateChat(180);
        chat.CountUnread = 180;
        chat.FirstUnreadMessageId = 1;
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        MessengerViewModel vm = null!;
        await ui.RunAsync(async () => vm = await OpenAsync(api, updates, ui));
        await ui.RunAsync(async () =>
        {
            Assert.True(vm.IsJumpDownVisible);
            if (unreadWasShown)
                vm.MessageBecameVisibleCommand.Execute(vm.Messages[0]);

            var openingScroll = vm.ScrollRequest;
            await vm.JumpDownCommand.ExecuteAsync(null);
            if (!unreadWasShown)
            {
                Assert.NotSame(openingScroll, vm.ScrollRequest);
                Assert.Equal(1, vm.ScrollRequest?.MessageId);
                Assert.Equal(51, vm.Messages[^1].Id);
                await vm.JumpDownCommand.ExecuteAsync(null);
            }

            Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest?.Target);
            Assert.Equal(180, vm.Messages[^1].Id);
            vm.FeedPositionChangedCommand.Execute(true);
            Assert.False(vm.IsJumpDownVisible);
            updates.Raise(new MessageUpdate("a", false, 181, MessageUpdateKind.New, Message: CreateMessage(181)));
            Assert.Equal(0, vm.NewMessagesBelowCount);
            vm.FeedPositionChangedCommand.Execute(false);
            updates.Raise(new MessageUpdate("a", false, 182, MessageUpdateKind.New, Message: CreateMessage(182)));
            Assert.Equal(1, vm.NewMessagesBelowCount);
            vm.FeedPositionChangedCommand.Execute(true);
            Assert.Equal(0, vm.NewMessagesBelowCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task JumpDownLoadsTailWithoutErrorWhenFirstUnreadWasDeleted()
    {
        var (api, chat, updates) = CreateChat(220);
        chat.CountUnread = 220;
        chat.FirstUnreadMessageId = 1;
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        await ui.RunAsync(async () =>
        {
            var vm = await OpenAsync(api, updates, ui);
            await vm.NavigateToMessageAsync(110);
            Assert.DoesNotContain(vm.Messages, row => row.Id == 1);
            api.Messages.RemoveAt(0);

            var errors = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.ActionError) && vm.ActionError is { } error)
                    errors.Add(error);
            };
            await vm.JumpDownCommand.ExecuteAsync(null);

            Assert.Empty(errors);
            Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest?.Target);
            Assert.Equal(220, vm.Messages[^1].Id);
            vm.Reset();
        });
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public async Task JumpDownCountsNewUnreadTailMessagesFromResync(bool atBottom, int expectedCount)
    {
        var (api, _, updates) = CreateChat(80);
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        await ui.RunAsync(async () =>
        {
            var vm = await OpenAsync(api, updates, ui);
            vm.FeedPositionChangedCommand.Execute(atBottom);
            var read = CreateMessage(82);
            read.ReadBy.Add(1);
            api.Messages.AddRange([CreateMessage(81), read, CreateMessage(83, sender: 1), CreateMessage(84)]);

            updates.Resync();

            Assert.Equal(84, vm.Messages[^1].Id);
            Assert.Equal(expectedCount, vm.NewMessagesBelowCount);
            updates.Resync();
            updates.Raise(new MessageUpdate("a", false, 84, MessageUpdateKind.New, Message: api.Messages[^1]));
            Assert.Equal(expectedCount, vm.NewMessagesBelowCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task JumpDownBadgeTracksNewUnreadIdsOutsideLoadedWindow()
    {
        var (api, chat, updates) = CreateChat(180);
        chat.CountUnread = 180;
        chat.FirstUnreadMessageId = 1;
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        MessengerViewModel vm = null!;
        await ui.RunAsync(async () => vm = await OpenAsync(api, updates, ui));
        ui.Post(() =>
        {
            void Incoming(long id, long sender = 2) =>
                updates.Raise(new MessageUpdate("a", false, id, MessageUpdateKind.New, Message: CreateMessage(id, sender)));

            Incoming(181);
            Incoming(181);
            Incoming(182);
            Incoming(183, sender: 1);
            Assert.Equal(2, vm.NewMessagesBelowCount);
            vm.FeedPositionChangedCommand.Execute(true);
            Assert.Equal(2, vm.NewMessagesBelowCount);
            updates.Raise(new MessageUpdate("a", false, 181, MessageUpdateKind.Delete));
            Assert.Equal(1, vm.NewMessagesBelowCount);
            updates.Raise(new MessageReadReceipt("a", 182, [1]));
            Assert.Equal(0, vm.NewMessagesBelowCount);
            Incoming(184);
            Assert.Equal(1, vm.NewMessagesBelowCount);
            vm.SelectedChat = vm.Chats.Single(item => item.Id == "b");
            Assert.Equal(0, vm.NewMessagesBelowCount);
            vm.Reset();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaginatesToBothEndsInServerOrder(bool fromUnread)
    {
        var (api, chat, updates) = CreateChat(181);
        foreach (var message in api.Messages)
        {
            message.SentAt.Seconds = (message.MessageId + 1) / 2;
            message.MessageId = 182 - message.MessageId;
        }
        var ordered = api.Messages.OrderBy(message => message.SentAt).ThenBy(message => message.MessageId).ToArray();
        if (fromUnread)
        {
            chat.CountUnread = 162;
            chat.FirstUnreadMessageId = ordered[19].MessageId;
        }
        var vm = await OpenAsync(api, updates);

        for (var page = 0; page < 5; page++)
        {
            await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
            await vm.LoadNewerHistoryCommand.ExecuteAsync(null);
        }

        Assert.Equal(ordered.Select(message => message.MessageId), vm.Messages.Select(message => message.Id));
        vm.Reset();
    }

    [Fact]
    public async Task PrivateTombstonesAdvanceTheCursorEvenWhenThePageAddsNoRows()
    {
        var (api, chat, updates) = CreateChat(0, isPrivate: true);
        api.PrivateMessages.AddRange(Enumerable.Range(1, 151).Select(id => new PrivateMessageModel
        {
            ChatId = chat.Id, MessageId = id, SentAt = new() { Seconds = id }, SenderId = 2,
            Text = "private", IsDeleted = id is >= 52 and <= 101
        }));
        api.PrivateHistoryPage = (_, anchor, before, after) => Task.FromResult(
            Slice(api.PrivateMessages, message => message.MessageId, anchor, before, after));
        var vm = await OpenAsync(api, updates);
        Assert.Equal(50, vm.Messages.Count);

        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.Equal(50, vm.Messages.Count);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);

        Assert.Equal(Enumerable.Range(1, 151).Where(id => id is < 52 or > 101).Select(id => (long)id),
            vm.Messages.Select(message => message.Id));
        vm.Reset();
    }

    [Fact]
    public async Task PageFallsBackToAdjacentAnchorAndJournalWinsOverItsSnapshot()
    {
        var (api, _, updates) = CreateChat(150);
        var vm = await OpenAsync(api, updates);
        api.Messages.RemoveAll(message => message.MessageId == 101);
        updates.Raise(new MessageUpdate("a", false, 101, MessageUpdateKind.Delete));
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        var requested = new List<long>();
        api.HistoryPage = (_, anchor, before, after) =>
        {
            requested.Add(anchor);
            return anchor == 102 ? response.Task : Task.FromResult(Slice(api.Messages, row => row.MessageId, anchor, before, after));
        };
        var loading = vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.Equal([101, 102], requested);
        var edit = CreateMessage(75);
        edit.IsEdited = true;
        edit.EditedAt = new() { Seconds = 500 };
        edit.Text = "edited while paging";
        updates.Raise(new MessageUpdate("a", false, 75, MessageUpdateKind.Edit, Message: edit));
        updates.Raise(new MessageUpdate("a", false, 76, MessageUpdateKind.Delete));

        response.SetResult(Slice(api.Messages, row => row.MessageId, 102, 50, 0));
        await loading;

        Assert.Equal("edited while paging", vm.Messages.Single(row => row.Id == 75).Text);
        Assert.DoesNotContain(vm.Messages, row => row.Id is 76 or 101);
        Assert.Equal(51, vm.Messages[0].Id);
        vm.Reset();
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("window")]
    [InlineData("reset")]
    [InlineData("account")]
    [InlineData("node")]
    public async Task LatePageCannotApplyToAnotherScope(string change)
    {
        var (api, _, updates) = CreateChat(180);
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        var vm = await OpenAsync(api, updates);
        var regularHistory = api.HistoryPage!;
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        api.HistoryPage = (id, anchor, before, after) =>
            id == "a" && anchor == 131 ? response.Task : regularHistory(id, anchor, before, after);
        var loading = vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.False(loading.IsCompleted);

        switch (change)
        {
            case "chat":
                vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
                break;
            case "window":
                await vm.NavigateToMessageAsync(1);
                break;
            case "reset":
                vm.Reset();
                break;
            case "account":
                api.CurrentUserId = 2;
                break;
            case "node":
                api.CurrentNodeAddress = "another-node";
                break;
        }

        var expected = vm.Messages.ToArray();
        var scroll = vm.ScrollRequest;
        response.SetResult(Slice(api.Messages, row => row.MessageId, 131, 50, 0));
        await loading;

        Assert.Equal(expected, vm.Messages);
        Assert.Same(scroll, vm.ScrollRequest);
        Assert.False(vm.HasHistoryError);
        vm.Reset();
    }

    [Fact]
    public async Task RepeatedViewportEventsCoalesceAndFailureRequiresExplicitRetry()
    {
        var (api, _, updates) = CreateChat(130);
        var vm = await OpenAsync(api, updates);
        var regularHistory = api.HistoryPage!;
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        var calls = 0;
        api.HistoryPage = (_, _, _, _) =>
        {
            calls++;
            return response.Task;
        };

        var loading = vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        response.SetResult((new(false), null));
        await loading;
        Assert.Equal(1, calls);
        var failedCalls = calls;
        Assert.True(vm.HasHistoryError);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.Equal(failedCalls, calls);

        api.HistoryPage = regularHistory;
        await vm.RetryHistoryCommand.ExecuteAsync(null);

        Assert.False(vm.HasHistoryError);
        Assert.Equal(31, vm.Messages[0].Id);
        vm.Reset();
    }

    [Fact]
    public async Task QuoteAndPinJumpOutsideBothEdgesAndMissingTargetKeepsWindow()
    {
        var (api, _, updates) = CreateChat(220);
        api.Messages[^1].ReplyTo = new() { MessageId = 4, TextPreview = "original" };
        api.Pins.Add(new() { Message = new Message { Id = 218, Content = new() { Text = "pin" } } });
        var vm = await OpenAsync(api, updates);

        await vm.ScrollToOriginalCommand.ExecuteAsync(vm.Messages[^1]);
        Assert.Equal(4, vm.ScrollRequest!.MessageId);
        Assert.Equal(Enumerable.Range(1, 54).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        await vm.CyclePinnedMessageCommand.ExecuteAsync(null);
        Assert.Equal(218, vm.ScrollRequest!.MessageId);
        Assert.Equal(168, vm.Messages[0].Id);

        var window = vm.Messages.ToArray();
        var scroll = vm.ScrollRequest;
        api.Messages.RemoveAll(row => row.MessageId == 4);
        await vm.ScrollToOriginalCommand.ExecuteAsync(vm.Messages[^1]);

        Assert.Equal("Messenger_HistoryMessageUnavailable", vm.ActionError);
        Assert.Equal(window, vm.Messages);
        Assert.Same(scroll, vm.ScrollRequest);
        vm.Reset();
    }

    [Fact]
    public async Task JumpTransportFailureKeepsWindowAndReportsLoadError()
    {
        var (api, _, updates) = CreateChat(150);
        var vm = await OpenAsync(api, updates);
        var window = vm.Messages.ToArray();
        api.HistoryPage = (_, _, _, _) => Task.FromResult<(ErrorReturner, List<MessageModel>?)>((new(false), null));

        await vm.NavigateToMessageAsync(1);

        Assert.Equal("Messenger_HistoryLoadFailed", vm.ActionError);
        Assert.Equal(window, vm.Messages);
        vm.Reset();
    }

    [Fact]
    public async Task PinSnapshotStillAppliesAfterJumpWithinSameChat()
    {
        var (api, _, updates) = CreateChat(220);
        var pins = new TaskCompletionSource<(ErrorReturner, List<PinnedMessageInfo>?)>();
        api.PinnedHistory = () => pins.Task;
        var vm = await OpenAsync(api, updates);
        await vm.NavigateToMessageAsync(4);

        var updated = new TaskCompletionSource();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.PinnedPreview))
            {
                updated.TrySetResult();
            }
        };
        pins.SetResult((new(true), [new() { Message = new Message { Id = 218, Content = new() { Text = "pin" } } }]));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(218, vm.PinnedPreview?.MessageId);
        await vm.CyclePinnedMessageCommand.ExecuteAsync(null);
        Assert.Equal(218, vm.ScrollRequest?.MessageId);
        vm.Reset();
    }

    [Fact]
    public async Task ReconnectPreservesSeveralPagesAndUsesBoundedRequests()
    {
        var (api, _, updates) = CreateChat(300);
        var vm = await OpenAsync(api, updates);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        var kept = vm.Messages.Single(row => row.Id == 131);
        var olderEdit = api.Messages.Single(row => row.MessageId == 111);
        olderEdit.Text = "edited offline";
        olderEdit.IsEdited = true;
        olderEdit.EditedAt = new() { Seconds = 400 };
        api.Messages.RemoveAll(row => row.MessageId == 179);
        api.Messages.Add(CreateMessage(301));
        var history = api.HistoryPage!;
        var calls = 0;
        api.HistoryPage = (id, anchor, before, after) =>
        {
            calls++;
            return history(id, anchor, before, after);
        };

        updates.Resync();

        Assert.Equal(Enumerable.Range(101, 201).Where(id => id != 179).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        Assert.Equal("edited offline", vm.Messages.Single(row => row.Id == 111).Text);
        Assert.Same(kept, vm.Messages.Single(row => row.Id == 131));
        Assert.InRange(calls, 2, 13);
        vm.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnSendOrForwardFromHistoryLoadsTrueTail(bool forward)
    {
        var (api, chat, updates) = CreateChat(180);
        chat.CountUnread = 180;
        chat.FirstUnreadMessageId = 1;
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var vm = await OpenAsync(api, updates, dispatcher);
        var incoming = CreateMessage(181);
        api.Messages.Add(incoming);
        updates.Raise(new IncomingMessage("a", incoming));
        Assert.DoesNotContain(vm.Messages, row => row.Id == 181);
        Assert.Equal(incoming.Text, vm.SelectedChat!.Preview);

        var sent = CreateMessage(182, sender: 1);
        var response = new TaskCompletionSource<(ErrorReturner, MessageModel?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.SendResponse = () => response.Task;
        if (forward)
        {
            vm.StartForwardMessageCommand.Execute(vm.Messages[0]);
            vm.ToggleForwardTargetCommand.Execute(vm.ForwardTargets.Single());
            await dispatcher.RunAsync(() => vm.SubmitForwardCommand.ExecuteAsync(null));
        }
        else
        {
            vm.DraftText = "send";
            await dispatcher.RunAsync(() => vm.SendCommand.ExecuteAsync(null));
        }

        Assert.Equal(Enumerable.Range(132, 50).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest!.Target);
        api.Messages.Add(sent);
        response.SetResult((new(true), sent));
        await MessengerTestDoubles.WaitForMessageAsync(vm, 182, dispatcher);
        await dispatcher.RunAsync(() => vm.ShowOutgoingTailCommand.ExecuteAsync(null));
        Assert.Equal(182, vm.Messages[^1].Id);
        dispatcher.Post(vm.Reset);
    }

    [Fact]
    public async Task FailedOutgoingMessageIsVisibleAndCanBeRetriedFromHistory()
    {
        var (api, _, updates) = CreateChat(180);
        var attempts = 0;
        api.SendOutgoing = (_, _) => Task.FromResult<(ErrorReturner, MessageModel?)>(++attempts == 1
            ? (new(false, errorResourceKey: "Messenger_UserUnavailable"), null)
            : (new(true), CreateMessage(181, sender: 1)));
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var vm = await OpenAsync(api, updates, dispatcher);
        vm.DraftText = "queued";
        await dispatcher.RunAsync(() => vm.SendCommand.ExecuteAsync(null));
        await MessengerTestDoubles.WaitForOutgoingAsync(vm,
            () => vm.PendingMessages.SingleOrDefault()?.CanRetry == true, dispatcher);
        Assert.Null(vm.OutgoingHistoryNotice);

        await dispatcher.RunAsync(() => vm.NavigateToMessageAsync(1));
        Assert.False(vm.IsOutgoingTailVisible);
        Assert.Equal("Messenger_OutboxHistoryFailed", vm.OutgoingHistoryNotice);
        await dispatcher.RunAsync(() => vm.ShowOutgoingTailCommand.ExecuteAsync(null));

        Assert.True(vm.IsOutgoingTailVisible);
        Assert.Null(vm.OutgoingHistoryNotice);
        await dispatcher.RunAsync(() => Assert.Single(vm.PendingMessages).RetryCommand.ExecuteAsync(null));
        await MessengerTestDoubles.WaitForMessageAsync(vm, 181, dispatcher);
        Assert.Equal(2, attempts);
        dispatcher.Post(vm.Reset);
    }

    [Fact]
    public async Task MultiChatForwardReloadsTailOnceAfterQueueingForTargets()
    {
        var (api, chat, updates) = CreateChat(180);
        chat.CountUnread = 180;
        chat.FirstUnreadMessageId = 1;
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var vm = await OpenAsync(api, updates, dispatcher);
        var history = api.HistoryPage!;
        var tailRequests = 0;
        api.HistoryPage = (id, anchor, before, after) =>
        {
            if (anchor == 0)
            {
                tailRequests++;
            }
            return history(id, anchor, before, after);
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = CreateMessage(181, sender: 1);
        var sends = new System.Collections.Concurrent.ConcurrentQueue<string>();
        api.SendOutgoing = async (message, _) =>
        {
            sends.Enqueue(message.ChatId);
            if (sends.Count == 2)
                bothStarted.TrySetResult();
            await release.Task;
            return (new(true), message.ChatId == "a" ? sent : MessengerTestDoubles.CreateMessage(182, "b", 1));
        };
        vm.StartForwardMessageCommand.Execute(vm.Messages[0]);
        foreach (var target in vm.ForwardTargets)
        {
            vm.ToggleForwardTargetCommand.Execute(target);
        }
        await dispatcher.RunAsync(() => vm.SubmitForwardCommand.ExecuteAsync(null));
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "a", "b" }, sends.Order());
        Assert.Equal(1, tailRequests);
        release.SetResult();
        await MessengerTestDoubles.WaitForMessageAsync(vm, 181, dispatcher);
        Assert.Equal(1, tailRequests);
        Assert.Equal(181, vm.Messages[^1].Id);
        dispatcher.Post(vm.Reset);
    }

    [Fact]
    public async Task PartiallyQueuedForwardKeepsStorageErrorAfterTailReload()
    {
        var (api, chat, updates) = CreateChat(180);
        chat.CountUnread = 180;
        chat.FirstUnreadMessageId = 1;
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        var queued = new List<string>();
        api.OutgoingStore.BeforeFirstSave = message =>
        {
            if (queued.Count > 0)
                throw new IOException("Storage failure");
            queued.Add(message.ChatId);
        };
        api.SendOutgoing = (_, token) => WaitForCancellationAsync(token);
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        MessengerViewModel vm = null!;
        await ui.RunAsync(async () => vm = await OpenAsync(api, updates, ui));
        await ui.RunAsync(async () =>
        {
            vm.StartForwardMessageCommand.Execute(vm.Messages[0]);
            foreach (var target in vm.ForwardTargets)
                vm.ToggleForwardTargetCommand.Execute(target);
            await vm.SubmitForwardCommand.ExecuteAsync(null);
            Assert.Single(queued);
            Assert.Equal("Messenger_OutboxStorageFailed", vm.ActionError);
            Assert.False(vm.IsOutgoingTailVisible);
            await vm.ShowOutgoingTailCommand.ExecuteAsync(null);
            Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest?.Target);
            Assert.Equal(180, vm.Messages[^1].Id);
            Assert.Equal("Messenger_OutboxStorageFailed", vm.ActionError);
            Assert.Single(api.OutgoingStore.ListOperations());
            Assert.Equal(0, vm.NewMessagesBelowCount);
            vm.Reset();
        });

        static async Task<(ErrorReturner, MessageModel?)> WaitForCancellationAsync(CancellationToken token)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return default;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForwardFailureIsVisibleAfterSwitchingChatsOnlyInTheSameSession(bool resetSession)
    {
        var (api, _, updates) = CreateChat(1);
        api.Chats.Add(MessengerTestDoubles.CreateChat("b"));
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var vm = await OpenAsync(api, updates, dispatcher);
        var response = new TaskCompletionSource<(ErrorReturner, MessageModel?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.SendOutgoing = (_, token) =>
        {
            sending.TrySetResult(token);
            return response.Task;
        };
        vm.StartForwardMessageCommand.Execute(vm.Messages[0]);
        vm.ToggleForwardTargetCommand.Execute(vm.ForwardTargets.Single(target => target.ChatId == "b"));
        await dispatcher.RunAsync(() => vm.SubmitForwardCommand.ExecuteAsync(null));
        var token = await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));

        dispatcher.Post(() => vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b"));
        if (resetSession)
        {
            await dispatcher.RunAsync(async () => Assert.True(await vm.ClearOutgoingForLogoutAsync()));
            dispatcher.Post(vm.Reset);
            await dispatcher.RunAsync(() => vm.LoadAsync());
            dispatcher.Post(() => vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b"));
            Assert.True(token.IsCancellationRequested);
        }
        response.SetResult((new(false, errorResourceKey: "Messenger_UserUnavailable"), null));
        if (resetSession)
            Assert.Empty(vm.PendingMessages);
        else
        {
            await MessengerTestDoubles.WaitForOutgoingAsync(vm,
                () => vm.PendingMessages.SingleOrDefault()?.CanRetry == true, dispatcher);
            Assert.Equal("Messenger_UserUnavailable", Assert.Single(vm.PendingMessages).Status);
        }
        Assert.Null(vm.ActionError);
        dispatcher.Post(vm.Reset);
    }

    [Fact]
    public async Task NewMessageRacingWithLastPageAppearsOnceTheTailIsConnected()
    {
        var (api, chat, updates) = CreateChat(100);
        chat.CountUnread = 100;
        chat.FirstUnreadMessageId = 1;
        var vm = await OpenAsync(api, updates);
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        api.HistoryPage = (_, _, _, _) => response.Task;
        var loading = vm.LoadNewerHistoryCommand.ExecuteAsync(null);
        var incoming = CreateMessage(101);
        updates.Raise(new IncomingMessage("a", incoming));
        Assert.DoesNotContain(vm.Messages, row => row.Id == 101);

        response.SetResult(Slice(api.Messages, row => row.MessageId, 51, 0, 50));
        await loading;

        Assert.Equal(Enumerable.Range(1, 101).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        vm.Reset();
    }

    [Fact]
    public async Task NewFederatedMessageInsidePageSurvivesOlderSnapshot()
    {
        var (api, _, updates) = CreateChat(150);
        var vm = await OpenAsync(api, updates);
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        api.HistoryPage = (_, _, _, _) => response.Task;

        // Окно вокруг цитаты оставляет хвост за пределами видимых сообщений.
        var navigate = vm.NavigateToMessageAsync(1);
        response.SetResult(Slice(api.Messages, row => row.MessageId, 1, 50, 50));
        await navigate;

        var page = Slice(api.Messages, row => row.MessageId, 51, 0, 50);
        response = new();
        var loading = vm.LoadNewerHistoryCommand.ExecuteAsync(null);
        var federated = CreateMessage(151);
        federated.SentAt = new() { Seconds = 75, Nanos = 500 };
        updates.Raise(new IncomingMessage("a", federated));
        response.SetResult(page);
        await loading;

        Assert.Equal(new long[] { 75, 151, 76 }, vm.Messages.Skip(74).Take(3).Select(row => row.Id));
        vm.Reset();
    }

    [Fact]
    public async Task TrimmedEndCanBeLoadedAgainWithoutGaps()
    {
        var (api, _, updates) = CreateChat(1100);
        var vm = await OpenAsync(api, updates);
        for (var page = 0; page < 22; page++)
        {
            await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        }

        Assert.Equal(Enumerable.Range(1, 1000).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        for (var page = 0; page < 3; page++)
        {
            await vm.LoadNewerHistoryCommand.ExecuteAsync(null);
        }

        Assert.Equal(Enumerable.Range(101, 1000).Select(id => (long)id), vm.Messages.Select(row => row.Id));
        vm.Reset();
    }

    [Fact]
    public async Task TransientlyVisibleRowIsNotAddedToReadBatch()
    {
        var (api, _, updates) = CreateChat(120);
        var vm = await OpenAsync(api, updates);
        var reading = new TaskCompletionSource<IReadOnlyCollection<long>>();
        api.MarkReadResponse = ids =>
        {
            reading.SetResult(ids);
            return Task.FromResult(new ErrorReturner(true));
        };

        var attempted = false;
        vm.Messages.CollectionChanged += (_, change) =>
        {
            if (change.NewItems?[0] is MessageItemViewModel { Id: 30 } transient)
            {
                Assert.True(vm.IsHistoryUpdating);
                attempted = true;
                vm.MessageBecameVisibleCommand.Execute(transient);
            }
        };
        await vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        Assert.True(attempted);

        vm.MessageBecameVisibleCommand.Execute(vm.Messages.Single(row => row.Id == 31));
        Assert.Equal([31L], await reading.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        vm.Reset();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(48, true)]
    [InlineData(49, false)]
    public void ViewportTriggersOnlyNearEitherEdge(double distance, bool expected) =>
        Assert.Equal(expected, MessageFeedViewport.IsNearEdge(distance));

    [Theory]
    [InlineData(100, 80, 60, 140, 500, 140)]
    [InlineData(0, 30, 40, 200, 500, 0)]
    [InlineData(250, 120, 40, 100, 300, 300)]
    public void JumpCentersVariableHeightRowsWithinScrollableRange(double offset, double top, double height,
        double viewport, double extent, double expected) =>
        Assert.Equal(expected, MessageFeedViewport.CenterOffset(offset, top, height, viewport, extent));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowJumpDuringSendStillClearsDraft(bool isPrivate)
    {
        var dispatcher = new InlineUiDispatcher(serializeContinuations: true);
        var (api, chat, updates) = CreateChat(160, isPrivate);
        api.PrivateMessages.AddRange(Enumerable.Range(1, 160).Select(id => new PrivateMessageModel
        {
            ChatId = chat.Id, MessageId = id, SentAt = new() { Seconds = id }, SenderId = 2, Text = "private"
        }));
        api.PrivateHistoryPage = (_, anchor, before, after) => Task.FromResult(
            Slice(api.PrivateMessages, message => message.MessageId, anchor, before, after));
        var regular = new TaskCompletionSource<(ErrorReturner, MessageModel?)>();
        var encrypted = new TaskCompletionSource<(ErrorReturner, PrivateMessageModel?)>();
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.OutgoingStore.AfterFirstSaveAsync = _ =>
        {
            writing.TrySetResult();
            return release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        api.SendResponse = () => regular.Task;
        api.PrivateSendResponse = () => encrypted.Task;
        var vm = await OpenAsync(api, updates, dispatcher);
        if (!isPrivate)
        {
            vm.StartReplyMessageCommand.Execute(vm.Messages[^1]);
        }
        vm.DraftText = "send";
        var sending = dispatcher.RunAsync(() => vm.SendCommand.ExecuteAsync(null));
        if (!isPrivate)
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.RunAsync(() => vm.NavigateToMessageAsync(1));
        Assert.Equal(1, vm.ScrollRequest?.MessageId);

        var sent = CreateMessage(161, sender: 1);
        api.Messages.Add(sent);
        var privateSent = new PrivateMessageModel
        {
            ChatId = chat.Id, MessageId = 161, SentAt = new() { Seconds = 161 }, SenderId = 1, Text = "send"
        };
        api.PrivateMessages.Add(privateSent);
        regular.SetResult((new(true), sent));
        encrypted.SetResult((new(true), privateSent));
        release.TrySetResult();
        await sending;
        if (!isPrivate)
            await MessengerTestDoubles.WaitForMessageAsync(vm, 161, dispatcher);

        dispatcher.Post(() =>
        {
            Assert.Empty(vm.DraftText);
            Assert.Null(vm.ReplyTarget);
            Assert.Equal(161, vm.Messages[^1].Id);
            Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest?.Target);
            vm.Reset();
        });
    }

    [Fact]
    public async Task WindowJumpDuringEditStillClosesComposer()
    {
        var (api, _, updates) = CreateChat(160);
        api.Messages[^1] = CreateMessage(160, sender: 1);
        var response = new TaskCompletionSource<(ErrorReturner, MessageModel?)>();
        api.EditResponse = () => response.Task;
        var vm = await OpenAsync(api, updates);
        vm.StartEditMessageCommand.Execute(vm.Messages[^1]);
        vm.DraftText = "edited";
        var editing = vm.SendCommand.ExecuteAsync(null);
        await vm.NavigateToMessageAsync(1);
        var edited = CreateMessage(160, sender: 1);
        edited.IsEdited = true;
        edited.Text = "edited";
        response.SetResult((new(true), edited));
        await editing;

        Assert.Null(vm.EditingMessage);
        Assert.Empty(vm.DraftText);
        vm.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowJumpDuringPinStillAppliesResult(bool success)
    {
        var (api, _, updates) = CreateChat(160);
        var response = new TaskCompletionSource<(ErrorReturner, PinnedMessageInfo?)>();
        api.PinResponse = () => response.Task;
        var vm = await OpenAsync(api, updates);
        var pinning = vm.TogglePinMessageCommand.ExecuteAsync(vm.Messages[^1]);
        await vm.NavigateToMessageAsync(1);
        response.SetResult(success
            ? (new(true), new() { Message = new Message { Id = 160, Content = new() { Text = "pin" } } })
            : (new(false, "pin failed"), null));
        await pinning;

        if (success)
        {
            Assert.Equal(160, vm.PinnedPreview?.MessageId);
        }
        else
        {
            Assert.Equal("pin failed", vm.ActionError);
        }
        vm.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncomingMessageFollowsTailWhileHistoryIsWaiting(bool resync)
    {
        var (api, _, updates) = CreateChat(150);
        var vm = await OpenAsync(api, updates);
        var response = new TaskCompletionSource<(ErrorReturner, List<MessageModel>?)>();
        var attachment = new TaskCompletionSource<string>();
        api.Messages[79].Attachments.Add(new() { Type = MessageAttachmentType.Document, FileId = "file" });
        api.FileUrl = _ => attachment.Task;
        var scroll = vm.ScrollRequest;
        Task loading;
        if (resync)
        {
            api.HistoryPage = (_, _, _, _) => response.Task;
            updates.Resync();
            loading = Task.CompletedTask;
        }
        else
        {
            loading = vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        }

        var incoming = CreateMessage(151);
        if (resync)
        {
            incoming.SentAt.Seconds = 149;
        }
        updates.Raise(new IncomingMessage("a", incoming));
        try
        {
            Assert.Contains(vm.Messages, row => row.Id == 151);
            Assert.NotSame(scroll, vm.ScrollRequest);
            Assert.Equal(MessageScrollTarget.Bottom, vm.ScrollRequest?.Target);
            Assert.False(vm.IsHistoryUpdating);
        }
        finally
        {
            vm.Reset();
            response.SetResult((new(false), null));
            attachment.SetResult("resolved");
            await loading;
        }
    }

    [Fact]
    public async Task PageBuildsRowsConcurrentlyAndAppliesOnlyAfterTheyAreReady()
    {
        var (api, _, updates) = CreateChat(130);
        var vm = await OpenAsync(api, updates);
        var first = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var secondStarted = new TaskCompletionSource();
        api.Messages[31].Attachments.Add(new() { Type = MessageAttachmentType.Document, FileId = "first" });
        api.Messages[32].Attachments.Add(new() { Type = MessageAttachmentType.Document, FileId = "second" });
        api.FileUrl = id =>
        {
            if (id == "first")
            {
                return first.Task;
            }
            secondStarted.TrySetResult();
            return second.Task;
        };
        var original = vm.Messages.ToArray();
        var loading = vm.LoadOlderHistoryCommand.ExecuteAsync(null);
        try
        {
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(original, vm.Messages);
            updates.Raise(new MessageUpdate("a", false, 32, MessageUpdateKind.Delete));
        }
        finally
        {
            first.SetResult("resolved");
            second.SetResult("resolved");
            await loading;
        }
        Assert.Contains(vm.Messages, row => row.Id == 33);
        Assert.DoesNotContain(vm.Messages, row => row.Id == 32);
        vm.Reset();
    }

    [Fact]
    public void LayoutGrowthAndResizePreserveTailFollowingUntilActualScroll()
    {
        var atBottom = (Offset: 400d, Extent: 1000d, Viewport: 600d);
        var expanded = (Offset: 410d, Extent: 1300d, Viewport: 600d);
        var resized = (Offset: 430d, Extent: 1300d, Viewport: 500d);
        var scrolled = (Offset: 400d, Extent: 1300d, Viewport: 500d);

        var following = Follow(true, atBottom, expanded);
        Assert.True(following);
        following = Follow(following, expanded, resized);
        Assert.True(following);
        following = Follow(following, resized, scrolled);
        Assert.False(following);
        Assert.False(Follow(following, scrolled, (400, 1300, 900)));
        Assert.True(Follow(following, scrolled, (790, 1300, 500)));

        static bool Follow(bool following, (double, double, double) before, (double Offset, double Extent, double Viewport) current) =>
            MessageFeedViewport.FollowTail(following, MessageFeedViewport.HasScrolled(before, current),
                current.Extent - current.Viewport - current.Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollWithChangingExtentStopsFollowingAndReopensHistoryEdgeOnlyWithInput(bool userInput)
    {
        var previous = (Offset: 400d, Extent: 1000d, Viewport: 600d);
        var current = (Offset: 20d, Extent: 1300d, Viewport: 600d);

        var scrolled = MessageFeedViewport.HasScrolled(previous, current, userInput);

        Assert.Equal(userInput, scrolled);
        Assert.Equal(!userInput, MessageFeedViewport.FollowTail(true, scrolled,
            current.Extent - current.Viewport - current.Offset));
        Assert.Equal(userInput, MessageFeedViewport.CheckHistoryEdge(false, scrolled,
            current.Offset, current.Extent, current.Viewport).Load);
        Assert.False(MessageFeedViewport.HasScrolled(previous, (400, 1300, 600), userInput));
    }

    [Fact]
    public void HistoryEdgeReopensAfterLeavingOrScrollingAndKeepsFillingShortFeed()
    {
        var first = MessageFeedViewport.CheckHistoryEdge(true, false, 0, 900, 600);
        Assert.True(first.Load);
        var unchanged = MessageFeedViewport.CheckHistoryEdge(first.Armed, false, 0, 1600, 600);
        Assert.False(unchanged.Load);
        Assert.True(MessageFeedViewport.CheckHistoryEdge(unchanged.Armed, true, 0, 1600, 600).Load);

        var away = MessageFeedViewport.CheckHistoryEdge(unchanged.Armed, false, 100, 1600, 600);
        Assert.False(away.Load);
        Assert.True(MessageFeedViewport.CheckHistoryEdge(away.Armed, false, 0, 1600, 600).Load);

        var shortFeed = MessageFeedViewport.CheckHistoryEdge(false, false, 0, 400, 600);
        Assert.True(shortFeed.Load);
        Assert.True(MessageFeedViewport.CheckHistoryEdge(shortFeed.Armed, false, 0, 500, 600).Load);
        Assert.False(MessageFeedViewport.CheckHistoryEdge(shortFeed.Armed, false, 0, 900, 600).Load);
    }

    private static (FakeMessengerService Api, Chat Chat, FakeRealtimeMessengerService Updates) CreateChat(int count, bool isPrivate = false)
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("a", chatType: isPrivate ? ChatType.Private : ChatType.Regular);
        if (isPrivate)
        {
            chat.PrivateInviteState = PrivateChatInviteState.Accepted;
        }
        api.Chats.Add(chat);
        api.Messages.AddRange(Enumerable.Range(1, count).Select(id => CreateMessage(id)));
        api.HistoryPage = (id, anchor, before, after) => Task.FromResult(Slice(api.Messages
            .Where(row => row.ChatId == id).OrderBy(row => row.SentAt).ThenBy(row => row.MessageId).ToList(),
            row => row.MessageId, anchor, before, after));
        return (api, chat, new());
    }

    private static async Task<MessengerViewModel> OpenAsync(FakeMessengerService api, FakeRealtimeMessengerService updates,
        InlineUiDispatcher? dispatcher = null)
    {
        var vm = MessengerTestDoubles.CreateViewModel(api, updates, keys: new() { Key = new byte[32] }, dispatcher: dispatcher);
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        return vm;
    }

    private static MessageModel CreateMessage(long id, long sender = 2)
    {
        var message = MessengerTestDoubles.CreateMessage(id, "a", sender, $"message {id}");
        message.SentAt = new() { Seconds = id };
        return message;
    }

    private static (ErrorReturner, List<T>?) Slice<T>(List<T> rows, Func<T, long> getId, long anchor, int before, int after)
    {
        if (anchor == 0)
        {
            return rows.Count == 0 ? (new(false, errorCode: 1), null) : (new(true), rows.TakeLast(before).ToList());
        }
        var index = rows.FindIndex(row => getId(row) == anchor);
        if (index < 0)
        {
            return (new(false, errorCode: ErrorReturner.HistoryAnchorNotFound), null);
        }
        var start = Math.Max(0, index - before);
        return (new(true), rows.Skip(start).Take(index - start + 1 + after).ToList());
    }
}
