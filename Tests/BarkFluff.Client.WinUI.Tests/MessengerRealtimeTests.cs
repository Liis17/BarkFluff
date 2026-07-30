using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerRealtimeTests
{
    [Fact]
    public async Task IncomingMessage_ForSelectedChat_InsertsInIdOrder()
    {
        var (viewModel, messenger, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(20, "a", 2)));
        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(10, "a", 2)));

        Assert.Equal([10, 20], viewModel.Messages.Select(message => message.Id));
        Assert.Empty(messenger.Messages);
    }

    [Fact]
    public async Task IncomingMessage_AlreadyShown_IsIgnored()
    {
        var (viewModel, _, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(5, "a", 1)));
        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(5, "a", 1)));

        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public async Task IncomingMessage_ForOtherChat_RaisesUnreadAndMovesChatToTop()
    {
        var (viewModel, _, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        var other = viewModel.Chats.Single(chat => chat.Id == "b");

        realtime.Raise(new IncomingMessage("b", MessengerTestDoubles.CreateMessage(7, "b", 3, "ping")));

        Assert.Same(other, viewModel.Chats[0]);
        Assert.Same(other, viewModel.VisibleChats[0]);
        Assert.Equal(1, other.UnreadCount);
        Assert.Equal(7, other.FirstUnreadMessageId);
        Assert.Equal("ping", other.Preview);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task IncomingMessage_WhenFeedIsScrolledUp_DoesNotScroll()
    {
        var (viewModel, _, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        viewModel.FeedPositionChangedCommand.Execute(false);
        viewModel.ScrollRequest = null;

        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(9, "a", 2)));

        Assert.Null(viewModel.ScrollRequest);
        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public async Task IncomingMessage_WhenFeedIsAtBottom_RequestsScrollEveryTime()
    {
        var (viewModel, _, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        viewModel.FeedPositionChangedCommand.Execute(true);

        var scrollRequests = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ScrollRequest) && viewModel.ScrollRequest is not null)
            {
                scrollRequests++;
            }
        };

        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(9, "a", 2)));
        realtime.Raise(new IncomingMessage("a", MessengerTestDoubles.CreateMessage(10, "a", 2)));

        // Оба запроса одинаковые по значению: без сброса в null второй не поднял бы уведомление.
        Assert.Equal(2, scrollRequests);
        Assert.Equal(new MessageScrollRequest(MessageScrollTarget.Bottom), viewModel.ScrollRequest);
    }

    [Fact]
    public async Task IncomingMessage_ForPrivateChat_UpdatesNothingButUnread()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("p", "Private", peerUserId: 4, chatType: ChatType.Private));
        var realtime = new FakeRealtimeMessengerService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime);
        await viewModel.LoadAsync();
        viewModel.SelectedChat = viewModel.Chats.Single();

        realtime.Raise(new IncomingMessage("p", MessengerTestDoubles.CreateMessage(3, "p", 4, "leak")));

        Assert.Empty(viewModel.Messages);
        Assert.Equal(string.Empty, viewModel.Chats[0].Preview);
        Assert.Equal(1, viewModel.Chats[0].UnreadCount);
    }

    [Fact]
    public async Task IncomingMessage_ForUnknownChat_AddsItToTheList()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        var realtime = new FakeRealtimeMessengerService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime);
        await viewModel.LoadAsync();

        messenger.Chats.Add(MessengerTestDoubles.CreateChat("c", "Carol", peerUserId: 5));
        realtime.Raise(new IncomingMessage("c", MessengerTestDoubles.CreateMessage(1, "c", 5)));

        Assert.Equal(["c", "a"], viewModel.Chats.Select(chat => chat.Id));
        Assert.Equal(2, viewModel.VisibleChats.Count);
    }

    [Fact]
    public async Task LoadAsync_TracksPresenceOfDirectChatPeersOnly()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("group", "Group"));
        var presence = new FakeOnlinePresenceService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, presence: presence);

        await viewModel.LoadAsync();

        Assert.Equal([2L], Assert.Single(presence.WatchedSets));
    }

    [Fact]
    public async Task PresenceChanged_UpdatesMatchingChat()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        var presence = new FakeOnlinePresenceService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, presence: presence);
        await viewModel.LoadAsync();

        presence.Raise(new UserPresence(2, IsOnline: true, DateTimeOffset.UtcNow));

        var chat = viewModel.Chats.Single();
        Assert.True(chat.IsOnline);
        Assert.True(chat.HasPresence);
        Assert.Equal("Messenger_StatusOnline", chat.PresenceLabel);
    }

    [Fact]
    public async Task SearchText_FiltersChatsButKeepsTheSelectedOne()
    {
        var (viewModel, _, _) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        // Выбранный чат не подходит под запрос, но остаётся в выборке: иначе список сбросил бы выбор.
        viewModel.SearchText = "bob";
        Assert.Equal(["a", "b"], viewModel.VisibleChats.Select(chat => chat.Id).Order());

        viewModel.SelectedChat = null;
        viewModel.SearchText = "bo";
        Assert.Equal(["b"], viewModel.VisibleChats.Select(chat => chat.Id));

        viewModel.SearchText = string.Empty;
        Assert.Equal(2, viewModel.VisibleChats.Count);
    }

    private static async Task<(BarkFluff.Client.Core.ViewModels.MessengerViewModel ViewModel, FakeMessengerService Messenger, FakeRealtimeMessengerService Realtime)> CreateLoadedAsync()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("b", "Bob", peerUserId: 3));
        var realtime = new FakeRealtimeMessengerService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime);
        await viewModel.LoadAsync();
        return (viewModel, messenger, realtime);
    }
}
