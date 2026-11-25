using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Time.Testing;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class UnreadBadgeTests
{
    [Fact]
    public async Task CountsAllUnmutedChatsAndPublishesOncePerBurst()
    {
        var api = new FakeMessengerService();
        var regular = MessengerTestDoubles.CreateChat("regular");
        regular.CountUnread = 2;
        var muted = MessengerTestDoubles.CreateChat("muted");
        muted.CountUnread = 5;
        muted.Muted = true;
        var privateChat = MessengerTestDoubles.CreateChat("private", chatType: ChatType.Private);
        privateChat.CountUnread = 100;
        var secret = MessengerTestDoubles.CreateChat("secret", chatType: ChatType.Secret);
        secret.CountUnread = 10;
        api.Chats.AddRange([regular, muted, privateChat, secret]);
        var realtime = new FakeRealtimeMessengerService();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui);
        vm.UnreadBadgeClock = clock;
        var published = new List<long>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.UnreadBadgeCount))
                published.Add(vm.UnreadBadgeCount);
        };

        await ui.RunAsync(vm.LoadAsync);
        ui.Post(() => vm.SearchText = "no matching chat");
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal([102L], published);

        realtime.Raise(new IncomingMessage(regular.Id, MessengerTestDoubles.CreateMessage(10, regular.Id, 2, "one")));
        realtime.Raise(new IncomingMessage(regular.Id, MessengerTestDoubles.CreateMessage(11, regular.Id, 2, "two")));
        await ui.RunAsync(() => vm.ToggleChatMuteCommand.ExecuteAsync(vm.Chats.Single(chat => chat.Id == muted.Id)));
        Assert.Equal([102L], published);
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal([102L, 109L], published);

        await ui.RunAsync(() => vm.ToggleChatMuteCommand.ExecuteAsync(vm.Chats.Single(chat => chat.Id == regular.Id)));
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal([102L, 109L, 105L], published);
        ui.Post(vm.Reset);
        Assert.Equal(0L, vm.UnreadBadgeCount);
    }

    [Theory]
    [InlineData(ChatType.Regular)]
    [InlineData(ChatType.Private)]
    public async Task ReadOnAnotherDeviceRefreshesUnopenedChat(ChatType kind)
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("unopened", chatType: kind);
        chat.CountUnread = 3;
        chat.FirstUnreadMessageId = 10;
        api.Chats.Add(chat);
        var realtime = new FakeRealtimeMessengerService();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui);
        vm.UnreadBadgeClock = clock;
        var confirmation = new TaskCompletionSource();
        vm.ResyncDelay = (_, token) => confirmation.Task.WaitAsync(token);

        await ui.RunAsync(vm.LoadAsync);
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal(3L, vm.UnreadBadgeCount);
        chat.CountUnread = 1;
        if (kind == ChatType.Regular)
            realtime.Raise(new MessageReadReceipt(chat.Id, 10, [1]));
        else
            realtime.Raise(new PrivateMessageReadReceipt(chat.Id, 1, 10));
        ui.Post(() => confirmation.SetResult());
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal(1L, vm.UnreadBadgeCount);
        ui.Post(vm.Reset);
    }

    [Fact]
    public async Task ReadReceiptsBeforeFirstUnreadSkipRefreshAndRelevantBurstRefreshesOnce()
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("unopened");
        chat.CountUnread = 3;
        chat.FirstUnreadMessageId = 10;
        api.Chats.Add(chat);
        var realtime = new FakeRealtimeMessengerService();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui);
        vm.UnreadBadgeClock = clock;
        vm.ResyncDelay = (delay, token) => Task.Delay(delay, clock, token);
        await ui.RunAsync(vm.LoadAsync);

        var requests = 0;
        api.ChatSnapshot = () =>
        {
            requests++;
            return Task.FromResult<(ErrorReturner, List<Chat>?)>((new(true), api.Chats));
        };

        ui.Post(() =>
        {
            realtime.Raise(new MessageReadReceipt(chat.Id, 9, [1, 2]));
            realtime.Raise(new MessageReadReceipt(chat.Id, 10, [2]));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(0, requests);

            chat.CountUnread = 1;
            chat.FirstUnreadMessageId = 12;
            realtime.Raise(new MessageReadReceipt(chat.Id, 10, [1]));
            realtime.Raise(new MessageReadReceipt(chat.Id, 11, [1]));
            realtime.Raise(new MessageReadReceipt(chat.Id, 11, [1, 2]));
            Assert.Equal(0, requests);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(1, requests);
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(1L, vm.UnreadBadgeCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task ReadOnAnotherDeviceUpdatesSelectedPrivateChatAndConfirmsRemainingUnread()
    {
        var api = new FakeMessengerService();
        var chat = MessengerTestDoubles.CreateChat("selected", chatType: ChatType.Private);
        chat.PrivateInviteState = PrivateChatInviteState.Accepted;
        chat.CountUnread = 3;
        chat.FirstUnreadMessageId = 10;
        api.Chats.Add(chat);
        api.PrivateMessages.Add(new() { ChatId = chat.Id, MessageId = 10, SenderId = 2, SentAt = new() { Seconds = 10 } });
        var realtime = new FakeRealtimeMessengerService();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, keys: new() { Key = new byte[32] }, dispatcher: ui);
        vm.UnreadBadgeClock = clock;
        vm.ResyncDelay = (delay, token) => Task.Delay(delay, clock, token);
        await ui.RunAsync(vm.LoadAsync);

        ui.Post(() =>
        {
            vm.SelectedChat = Assert.Single(vm.Chats);
            var message = Assert.Single(vm.Messages);
            Assert.False(message.IsReadByCurrentUser);
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(3L, vm.UnreadBadgeCount);

            chat.CountUnread = 1;
            chat.FirstUnreadMessageId = 12;
            realtime.Raise(new PrivateMessageReadReceipt(chat.Id, 1, 11));
            Assert.True(message.IsReadByCurrentUser);
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(2L, vm.UnreadBadgeCount);

            clock.Advance(TimeSpan.FromSeconds(1));
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(1L, vm.UnreadBadgeCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task MuteExpiryRestoresUnreadBadge()
    {
        var clock = new FakeTimeProvider();
        var api = new FakeMessengerService
        {
            MutedChats = [new() { ChatId = "muted", MutedUntil = Timestamp.FromDateTimeOffset(clock.GetUtcNow().AddMinutes(1)) }]
        };
        var chat = MessengerTestDoubles.CreateChat("muted");
        chat.CountUnread = 3;
        api.Chats.Add(chat);
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var vm = MessengerTestDoubles.CreateViewModel(api, dispatcher: ui, timeProvider: clock);
        vm.UnreadBadgeClock = clock;
        await ui.RunAsync(vm.LoadAsync);

        ui.Post(() =>
        {
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.True(Assert.Single(vm.Chats).IsMuted);
            Assert.Equal(0L, vm.UnreadBadgeCount);
            clock.Advance(TimeSpan.FromMinutes(1));
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.False(Assert.Single(vm.Chats).IsMuted);
            Assert.Equal(3L, vm.UnreadBadgeCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task CacheFeedsBadgeAndAccountSwitchCancelsPendingPublication()
    {
        var api = new FakeMessengerService { ChatsFail = true };
        var cached = MessengerTestDoubles.CreateChat("cached");
        cached.CountUnread = 7;
        var row = ChatListCacheTests.Row(cached.Id, cached.Title) with { Definition = cached.ToByteArray() };
        var cache = new ChatListCache(new ChatListCacheTests.MemoryCache { Snapshot = new([row], []) });
        var realtime = new FakeRealtimeMessengerService();
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui, chatCache: cache);
        vm.UnreadBadgeClock = clock;

        await ui.RunAsync(vm.LoadAsync);
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal(7L, vm.UnreadBadgeCount);

        api.ChatsFail = false;
        cached.CountUnread = 4;
        api.Chats.Add(cached);
        realtime.Resync();
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal(4L, vm.UnreadBadgeCount);

        realtime.Raise(new IncomingMessage(cached.Id, MessengerTestDoubles.CreateMessage(10, cached.Id, 2, "pending")));
        api.CurrentUserId = 2;
        api.Chats.Clear();
        await ui.RunAsync(vm.LoadAsync);
        Assert.Equal(0L, vm.UnreadBadgeCount);
        clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
        Assert.Equal(0L, vm.UnreadBadgeCount);
        ui.Post(vm.Reset);
    }
}
