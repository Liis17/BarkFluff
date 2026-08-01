using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerConnectionTests
{
    [Fact]
    public async Task RealtimeDisconnect_ReplacesPresenceLabelWithIndicator()
    {
        var (viewModel, realtime, _) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        realtime.RaiseConnection(false);

        Assert.True(viewModel.IsReconnecting);
        Assert.False(viewModel.IsPresenceLabelVisible);
    }

    /// <summary>Стрим присутствия идёт отдельным каналом, его обрыв обязан быть виден так же.</summary>
    [Fact]
    public async Task PresenceDisconnect_ShowsIndicator()
    {
        var (viewModel, _, presence) = await CreateLoadedAsync();

        presence.RaiseConnection(false);

        Assert.True(viewModel.IsReconnecting);
    }

    [Fact]
    public async Task PartialRecovery_KeepsIndicator()
    {
        var (viewModel, realtime, presence) = await CreateLoadedAsync();

        realtime.RaiseConnection(false);
        presence.RaiseConnection(false);
        realtime.RaiseConnection(true);

        Assert.True(viewModel.IsReconnecting);
    }

    [Fact]
    public async Task FullRecovery_RestoresPresenceLabel()
    {
        var (viewModel, realtime, presence) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        realtime.RaiseConnection(false);
        presence.RaiseConnection(false);
        realtime.RaiseConnection(true);
        presence.RaiseConnection(true);

        Assert.False(viewModel.IsReconnecting);
        Assert.True(viewModel.IsPresenceLabelVisible);
    }

    [Fact]
    public async Task GroupChat_HasNoPresenceLabelEvenWhenConnected()
    {
        var (viewModel, _, _) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "group");

        Assert.False(viewModel.IsReconnecting);
        Assert.False(viewModel.IsPresenceLabelVisible);
    }

    /// <summary>Индикатор всегда ровно один: в шапке чата либо над списком, но не оба сразу.</summary>
    [Fact]
    public async Task Reconnecting_ShowsExactlyOneIndicator()
    {
        var (viewModel, realtime, _) = await CreateLoadedAsync();
        realtime.RaiseConnection(false);

        Assert.False(viewModel.IsChatHeaderReconnectingVisible);
        Assert.True(viewModel.IsChatListReconnectingVisible);

        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        Assert.True(viewModel.IsChatHeaderReconnectingVisible);
        Assert.False(viewModel.IsChatListReconnectingVisible);
    }

    [Fact]
    public async Task Connected_ShowsNoIndicatorAtAll()
    {
        var (viewModel, _, _) = await CreateLoadedAsync();

        Assert.False(viewModel.IsChatHeaderReconnectingVisible);
        Assert.False(viewModel.IsChatListReconnectingVisible);
    }

    private static async Task<(MessengerViewModel ViewModel, FakeRealtimeMessengerService Realtime, FakeOnlinePresenceService Presence)> CreateLoadedAsync()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("group", "Team"));
        var realtime = new FakeRealtimeMessengerService();
        var presence = new FakeOnlinePresenceService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime, presence);
        await viewModel.LoadAsync();
        return (viewModel, realtime, presence);
    }
}
