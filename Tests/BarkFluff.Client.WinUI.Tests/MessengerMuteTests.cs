using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using BarkFluff.WebApi.Core;
using Google.Protobuf.WellKnownTypes;
using static BarkFluff.Client.WinUI.Tests.MessengerTestDoubles;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerMuteTests
{
    [Theory]
    [InlineData(ChatType.Regular, 2L)]
    [InlineData(ChatType.Regular, 0L)]
    [InlineData(ChatType.Private, 2L)]
    public async Task ToggleSendsBothValuesAndUpdatesUnreadIndicator(ChatType type, long peer)
    {
        var messenger = new FakeMessengerService();
        var definition = CreateChat("chat", peerUserId: peer, chatType: type);
        definition.CountUnread = 3;
        messenger.Chats.Add(definition);
        var requests = new List<(string, bool)>();
        messenger.ChangeMute = (id, muted) =>
        {
            requests.Add((id, muted));
            return Task.FromResult(new ErrorReturner(true));
        };
        var vm = CreateViewModel(messenger);
        await vm.LoadAsync();
        var chat = Assert.Single(vm.Chats);

        await vm.ToggleChatMuteCommand.ExecuteAsync(chat);
        Assert.True(vm.IsChatMuted(chat.Id));
        Assert.True(chat.HasMutedUnreadMessages);
        Assert.Equal(3, chat.UnreadCount);
        Assert.Equal("Messenger_UnmuteChat", chat.MuteMenuHeader);

        await vm.ToggleChatMuteCommand.ExecuteAsync(chat);
        Assert.False(chat.IsMuted);
        Assert.True(chat.HasUnmutedUnreadMessages);
        Assert.Equal("Messenger_MuteChat", chat.MuteMenuHeader);
        Assert.Equal(new[] { ("chat", true), ("chat", false) }, requests);
    }

    [Fact]
    public async Task LoadRestoresMuteAndResyncIgnoresSnapshotStartedBeforeWriteCompleted()
    {
        var messenger = new FakeMessengerService { MutedChats = [new() { ChatId = "chat" }, new() { ChatId = "off-page" }] };
        var definition = CreateChat("chat");
        definition.Muted = true;
        messenger.Chats.Add(definition);
        var realtime = new FakeRealtimeMessengerService();
        var vm = CreateViewModel(messenger, realtime);
        await vm.LoadAsync();
        var chat = Assert.Single(vm.Chats);
        Assert.True(chat.IsMuted);
        Assert.True(vm.IsChatMuted("off-page"));

        var stale = new TaskCompletionSource<(ErrorReturner, List<Chat>?)>();
        var staleMutes = new TaskCompletionSource<(ErrorReturner, List<MutedChat>?)>();
        messenger.MuteSnapshot = () => staleMutes.Task;
        messenger.ChatSnapshot = () => stale.Task;
        var loading = vm.LoadAsync();
        await vm.ToggleChatMuteCommand.ExecuteAsync(chat);
        var marker = CreateChat("marker");
        stale.SetResult((new ErrorReturner(true), [definition, marker]));
        Assert.Contains(vm.Chats, item => item.Id == marker.Id);
        await loading;
        var markerItem = vm.Chats.Single(item => item.Id == marker.Id);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        markerItem.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(markerItem.IsMuted) && markerItem.IsMuted)
            {
                applied.TrySetResult();
            }
        };
        staleMutes.SetResult((new(true),
            [new() { ChatId = chat.Id }, new() { ChatId = marker.Id }, new() { ChatId = "off-page" }]));
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(chat.IsMuted);
        Assert.True(vm.IsChatMuted("off-page"));

        messenger.ChatSnapshot = null;
        messenger.MuteSnapshot = null;
        messenger.MutedChats = [new() { ChatId = chat.Id }];
        realtime.Resync();
        Assert.Equal(2, vm.Chats.Count);
        Assert.Same(chat, vm.Chats.Single(item => item.Id == chat.Id));
        Assert.True(chat.IsMuted);
        Assert.False(vm.IsChatMuted("off-page"));
    }

    [Fact]
    public async Task ExpiryUsesOneTimerForNearestUtcDeadline()
    {
        var clock = new MuteClock();
        var messenger = new FakeMessengerService
        {
            MutedChats =
            [
                new() { ChatId = "chat", MutedUntil = Timestamp.FromDateTimeOffset(clock.Now.AddMinutes(1)) },
                new() { ChatId = "off-page", MutedUntil = Timestamp.FromDateTimeOffset(clock.Now.AddMinutes(2)) }
            ]
        };
        var definition = CreateChat("chat");
        definition.Muted = true;
        messenger.Chats.Add(definition);
        var vm = CreateViewModel(messenger, timeProvider: clock);
        await vm.LoadAsync();
        var chat = Assert.Single(vm.Chats);
        Assert.True(chat.IsMuted);
        Assert.True(vm.IsChatMuted("off-page"));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(chat.IsMuted);
        Assert.True(vm.IsChatMuted("off-page"));
        Assert.Equal(clock.Now.AddMinutes(1), clock.Due);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(vm.IsChatMuted("off-page"));
        Assert.Null(clock.Due);
        vm.Reset();
        Assert.Null(clock.Callback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteRollsBackAndDuplicateRequestIsIgnored(bool defaultResponse)
    {
        var response = new TaskCompletionSource<ErrorReturner>();
        var requests = 0;
        var messenger = new FakeMessengerService
        {
            ChangeMute = (_, _) =>
            {
                requests++;
                return response.Task;
            }
        };
        messenger.Chats.Add(CreateChat("chat"));
        var vm = CreateViewModel(messenger);
        await vm.LoadAsync();
        var chat = Assert.Single(vm.Chats);

        var changing = vm.ToggleChatMuteCommand.ExecuteAsync(chat);
        Assert.True(chat.IsMuted);
        await vm.ToggleChatMuteCommand.ExecuteAsync(chat);
        Assert.Equal(1, requests);
        response.SetResult(defaultResponse ? null! : new ErrorReturner(false));
        await changing;

        Assert.False(vm.IsChatMuted(chat.Id));
        Assert.False(chat.IsMuted);
        Assert.Equal("Messenger_ChatMuteFailed", vm.ActionError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionChangeHidesMuteAndIgnoresLateFailure(bool reset)
    {
        var response = new TaskCompletionSource<ErrorReturner>();
        var messenger = new FakeMessengerService
        {
            ChangeMute = (_, _) => response.Task,
            MutedChats = [new() { ChatId = "off-page" }]
        };
        messenger.Chats.Add(CreateChat("chat"));
        var vm = CreateViewModel(messenger);
        await vm.LoadAsync();
        var changing = vm.ToggleChatMuteCommand.ExecuteAsync(Assert.Single(vm.Chats));
        if (reset)
        {
            vm.Reset();
        }
        else
        {
            messenger.CurrentUserId = 2;
            messenger.CurrentNodeAddress = "other";
        }
        Assert.False(vm.IsChatMuted("chat"));
        Assert.False(vm.IsChatMuted("off-page"));
        messenger.MutedChats.Clear();
        await vm.LoadAsync();
        response.SetResult(new ErrorReturner(false));
        await changing;
        Assert.False(Assert.Single(vm.Chats).IsMuted);
        Assert.Null(vm.ActionError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableMuteSnapshotRetainsExpiryAndAcceptsPositiveHints(bool defaultResponse)
    {
        var clock = new MuteClock();
        var messenger = new FakeMessengerService
        {
            MutedChats = [new() { ChatId = "chat", MutedUntil = Timestamp.FromDateTimeOffset(clock.Now.AddMinutes(1)) }]
        };
        var definition = CreateChat("chat");
        definition.Muted = true;
        messenger.Chats.Add(definition);
        var vm = CreateViewModel(messenger, timeProvider: clock);
        await vm.LoadAsync();

        messenger.MuteSnapshot = () => Task.FromResult<(ErrorReturner, List<MutedChat>?)>(
            defaultResponse ? default : (new(false), null));
        definition.Muted = false;
        var fallback = CreateChat("fallback");
        fallback.Muted = true;
        messenger.Chats.Add(fallback);
        await vm.LoadAsync();

        Assert.True(vm.IsChatMuted("chat"));
        Assert.True(vm.IsChatMuted("fallback"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(vm.IsChatMuted("chat"));

        messenger.MuteSnapshot = null;
        messenger.MutedChats = [];
        await vm.LoadAsync();
        Assert.False(vm.IsChatMuted("fallback"));
    }

    [Fact]
    public async Task SearchUsesAuthoritativeMuteForChatOutsideFirstPage()
    {
        var clock = new MuteClock();
        var messenger = new FakeMessengerService
        {
            PersonChatId = "off-page",
            MutedChats = [new() { ChatId = "off-page", MutedUntil = Timestamp.FromDateTimeOffset(clock.Now.AddMinutes(1)) }]
        };
        var vm = CreateViewModel(messenger, timeProvider: clock);
        await vm.LoadAsync();

        var definition = CreateChat("off-page", peerUserId: 7);
        messenger.Chats.Add(definition);
        await vm.OpenSearchResultAsync(new UserSearchResultViewModel(
            new UserData { Id = 7 }, new StubLocalizationService()));
        Assert.True(vm.SelectedChat?.IsMuted);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(vm.SelectedChat?.IsMuted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingMuteDoesNotDelayChatListOrError(bool chatsFail)
    {
        var mutes = new TaskCompletionSource<(ErrorReturner, List<MutedChat>?)>();
        var messenger = new FakeMessengerService
        {
            MuteSnapshot = () => mutes.Task,
            ChatSnapshot = () => Task.FromResult<(ErrorReturner, List<Chat>?)>(
                chatsFail ? (new(false, "ListChats failed"), null) : (new(true), [CreateChat("chat")]))
        };
        var vm = CreateViewModel(messenger);
        try
        {
            var loading = vm.LoadAsync();
            if (chatsFail)
            {
                Assert.Equal("ListChats failed", vm.ActionError);
            }
            else
            {
                Assert.Equal("chat", Assert.Single(vm.Chats).Id);
            }
            await loading;
            Assert.False(mutes.Task.IsCompleted);
        }
        finally
        {
            vm.Reset();
            mutes.TrySetResult((new(true), []));
        }
    }

    [Fact]
    public async Task PendingMuteDoesNotDelayFeedResyncOrStartAnotherMuteRead()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(CreateChat("chat"));
        messenger.Messages.Add(CreateMessage(1, "chat", 2));
        var realtime = new FakeRealtimeMessengerService();
        var vm = CreateViewModel(messenger, realtime);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        Assert.Single(vm.Messages);

        var mutes = new TaskCompletionSource<(ErrorReturner, List<MutedChat>?)>();
        var requests = 0;
        messenger.MuteSnapshot = () =>
        {
            requests++;
            return mutes.Task;
        };
        messenger.Messages.Add(CreateMessage(2, "chat", 2));
        try
        {
            realtime.Resync();
            Assert.Contains(vm.Messages, message => message.Id == 2);
            var loading = vm.LoadAsync();
            Assert.True(loading.IsCompletedSuccessfully);
            Assert.Equal(1, requests);
        }
        finally
        {
            vm.Reset();
            mutes.TrySetResult((new(true), []));
        }
    }

    private sealed class MuteClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset? Due { get; private set; }
        public TimerCallback? Callback { get; private set; }
        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Null(Callback);
            Callback = callback;
            var timer = new MuteTimer(this);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            Now += elapsed;
            if (Due <= Now)
            {
                Due = null;
                Callback?.Invoke(null);
            }
        }

        private sealed class MuteTimer(MuteClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                clock.Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
                return true;
            }

            public void Dispose()
            {
                clock.Callback = null;
                clock.Due = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
