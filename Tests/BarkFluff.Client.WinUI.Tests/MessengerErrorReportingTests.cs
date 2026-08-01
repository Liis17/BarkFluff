using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class MessengerErrorReportingTests
{
    [Fact]
    public async Task LoadAsync_WhenChatsFail_ShowsError()
    {
        var messenger = CreateMessenger();
        messenger.ChatsFail = true;
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger);

        await viewModel.LoadAsync();

        Assert.Equal("чаты недоступны", viewModel.ActionError);
    }

    [Fact]
    public async Task OpenChat_WhenMessagesFail_ShowsError()
    {
        var (viewModel, messenger, _) = await CreateLoadedAsync();
        messenger.MessagesFail = true;

        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");

        Assert.Equal("история недоступна", viewModel.ActionError);
    }

    /// <summary>Черновик обязан пережить неудачу: иначе пользователь теряет набранный текст.</summary>
    [Fact]
    public async Task SendAsync_WhenSendFails_ShowsErrorAndKeepsDraft()
    {
        var (viewModel, messenger, _) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        messenger.SendFails = true;
        viewModel.DraftText = "черновик";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("сообщение не отправлено", viewModel.ActionError);
        Assert.Equal("черновик", viewModel.DraftText);
    }

    [Fact]
    public async Task SwitchingChat_ClearsPreviousError()
    {
        var (viewModel, messenger, _) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        messenger.SendFails = true;
        viewModel.DraftText = "черновик";
        await viewModel.SendCommand.ExecuteAsync(null);
        messenger.SendFails = false;

        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "b");

        Assert.Null(viewModel.ActionError);
    }

    // Приход опоздавшего ответа по уже покинутому чату тестом не покрыт: дублёры отвечают
    // синхронно, и загрузка успевает целиком до смены чата. Порядок условий в
    // LoadMessagesAsync проверен чтением кода.

    [Fact]
    public async Task UnknownChatAppend_WhenChatsFail_ReportsBackgroundError()
    {
        var (viewModel, messenger, realtime) = await CreateLoadedAsync();
        messenger.ChatsFail = true;

        realtime.Raise(new IncomingMessage("unknown", MessengerTestDoubles.CreateMessage(1, "unknown", 2)));

        Assert.Equal("чаты недоступны", viewModel.ActionError);
    }

    /// <summary>Ядро защиты от мигания: фон не перетирает сообщение о действии пользователя.</summary>
    [Fact]
    public async Task BackgroundError_DoesNotOverwriteActionError()
    {
        var (viewModel, messenger, realtime) = await CreateLoadedAsync();
        viewModel.SelectedChat = viewModel.Chats.Single(chat => chat.Id == "a");
        messenger.SendFails = true;
        viewModel.DraftText = "черновик";
        await viewModel.SendCommand.ExecuteAsync(null);

        messenger.ChatsFail = true;
        realtime.Raise(new IncomingMessage("unknown", MessengerTestDoubles.CreateMessage(1, "unknown", 2)));

        Assert.Equal("сообщение не отправлено", viewModel.ActionError);
    }

    private static FakeMessengerService CreateMessenger()
    {
        var messenger = new FakeMessengerService();
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alice", peerUserId: 2));
        messenger.Chats.Add(MessengerTestDoubles.CreateChat("b", "Bob", peerUserId: 3));
        return messenger;
    }

    private static async Task<(MessengerViewModel ViewModel, FakeMessengerService Messenger, FakeRealtimeMessengerService Realtime)> CreateLoadedAsync()
    {
        var messenger = CreateMessenger();
        var realtime = new FakeRealtimeMessengerService();
        var viewModel = MessengerTestDoubles.CreateViewModel(messenger, realtime);
        await viewModel.LoadAsync();
        return (viewModel, messenger, realtime);
    }
}
