using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ProfileViewModelTests
{
    [Fact]
    public async Task LoadOwnAsync_ShowsEmailAndNoPresence()
    {
        var messenger = new FakeMessengerService
        {
            UserData = new UserData
            {
                Id = 1,
                Username = "alice",
                FirstName = "Alice",
                LastName = "Smith",
                Email = "alice@example.com",
                RegistrationDate = new DateTime(2025, 5, 12, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadOwnAsync();

        Assert.True(viewModel.IsOwnProfile);
        Assert.Equal("Alice Smith", viewModel.DisplayName);
        Assert.Equal("@alice", viewModel.Username);
        Assert.Equal("alice@example.com", viewModel.Email);
        Assert.Equal("AS", viewModel.Initials);
        Assert.NotEmpty(viewModel.RegisteredAtLabel);
        Assert.Equal(string.Empty, viewModel.PresenceLabel);
    }

    [Fact]
    public async Task LoadOwnAsync_ResolvesSelfChatAndShowsAttachments()
    {
        var messenger = new FakeMessengerService
        {
            UserData = new UserData { Id = 1, Username = "alice" },
            PersonChatId = "self-chat"
        };
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadOwnAsync();

        Assert.True(viewModel.HasAttachments);
    }

    [Fact]
    public async Task LoadOwnAsync_NoSelfChatYet_HidesAttachments()
    {
        var messenger = new FakeMessengerService
        {
            UserData = new UserData { Id = 1, Username = "alice" },
            PersonChatIdFails = true
        };
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadOwnAsync();

        Assert.False(viewModel.HasAttachments);
    }

    [Fact]
    public async Task LoadForPeerAsync_TakesPresenceFromTheMessengerSubscriptionAndUsesGivenChatId()
    {
        var messenger = new FakeMessengerService
        {
            UserData = new UserData { Id = 7, Username = "bob", FirstName = "Bob" }
        };
        var presence = new FakeOnlinePresenceService();
        presence.Seed(new UserPresence(7, IsOnline: true, DateTimeOffset.UtcNow));
        var viewModel = new ProfileViewModel(messenger, presence, new StubLocalizationService());

        await viewModel.LoadForPeerAsync(7, "chat-with-bob");

        Assert.False(viewModel.IsOwnProfile);
        Assert.Equal("Bob", viewModel.DisplayName);
        // Почту сервер для чужого профиля не отдаёт — строка прячется по пустому значению.
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal("Messenger_StatusOnline", viewModel.PresenceLabel);
        Assert.True(viewModel.HasAttachments);
    }

    [Fact]
    public async Task LoadOwnAsync_Failure_ReportsError()
    {
        var messenger = new FakeMessengerService { UserDataFails = true };
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadOwnAsync();

        Assert.Equal("Error_ProfileLoadFailed", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadForPeerAsync_LoadsFirstAttachmentTabAutomatically()
    {
        var messenger = new FakeMessengerService { UserData = new UserData { Id = 7, Username = "bob" } };
        messenger.Attachments.Add(MessengerTestDoubles.CreateAttachmentInfo(1, MessageAttachmentType.Image));
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadForPeerAsync(7, "chat-with-bob");

        // Вкладка «Фото» — первая (индекс 0), поэтому грузится сразу вместе с профилем,
        // а не только при явном выборе.
        Assert.Single(viewModel.PhotosTab.Items);
        Assert.Empty(viewModel.VideosTab.Items);
    }
}
