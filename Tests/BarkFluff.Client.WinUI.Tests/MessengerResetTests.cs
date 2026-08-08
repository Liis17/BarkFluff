namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerResetTests
{
    /// <summary>
    /// Страница мессенджера закэширована, а ViewModel — синглтон, поэтому после выхода из
    /// аккаунта следующий вошедший увидел бы чужие чаты. Перезагрузка от этого не спасает:
    /// она чистит список только при успешном ответе и не трогает открытую переписку.
    /// </summary>
    [Fact]
    public async Task Reset_ClearsEverythingLeftFromThePreviousSession()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("b", "Bob", peerUserId: 3));
        messenger.UserData = new BarkFluff.WebApi.Core.MessengerData.NonSavedData.UserData { Id = 1, Username = "alice" };
        messenger.PersonChatId = "self-chat";
        var realtime = new FakeRealtimeMessengerService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime);
        await viewModel.LoadAsync();
        viewModel.SelectedChat = viewModel.Chats[0];
        viewModel.DraftText = "unsent";
        viewModel.SearchText = "ali";
        realtime.RaiseConnection(false);
        await viewModel.OpenOwnProfileCommand.ExecuteAsync(null);

        viewModel.Reset();

        Assert.Empty(viewModel.Chats);
        Assert.Empty(viewModel.VisibleChats);
        Assert.Empty(viewModel.Messages);
        Assert.Null(viewModel.SelectedChat);
        Assert.Equal(string.Empty, viewModel.DraftText);
        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.False(viewModel.IsPrivateUnlockVisible);
        Assert.Equal(string.Empty, viewModel.PrivatePassphrase);
        // Циклы остановлены вместе с сессией: «переподключение…» иначе залипло бы навсегда.
        Assert.False(viewModel.IsReconnecting);
        // ProfileViewModel — синглтон внутри синглтона: без сброса следующий вошедший
        // увидел бы в оверлее имя и вложения прошлого пользователя.
        Assert.False(viewModel.IsProfileVisible);
        Assert.Equal(string.Empty, viewModel.Profile.DisplayName);
        Assert.False(viewModel.Profile.HasAttachments);
    }
}
