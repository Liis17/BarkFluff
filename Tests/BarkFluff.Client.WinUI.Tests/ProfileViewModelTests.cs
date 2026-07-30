using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ProfileViewModelTests
{
    [Fact]
    public async Task LoadAsync_OwnProfile_ShowsEmailAndNoPresence()
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

        await viewModel.LoadAsync(0);

        Assert.True(viewModel.IsOwnProfile);
        Assert.Equal("Alice Smith", viewModel.DisplayName);
        Assert.Equal("@alice", viewModel.Username);
        Assert.Equal("alice@example.com", viewModel.Email);
        Assert.Equal("AS", viewModel.Initials);
        Assert.NotEmpty(viewModel.RegisteredAtLabel);
        Assert.Equal(string.Empty, viewModel.PresenceLabel);
    }

    [Fact]
    public async Task LoadAsync_PeerProfile_TakesPresenceFromTheMessengerSubscription()
    {
        var messenger = new FakeMessengerService
        {
            UserData = new UserData { Id = 7, Username = "bob", FirstName = "Bob" }
        };
        var presence = new FakeOnlinePresenceService();
        presence.Seed(new UserPresence(7, IsOnline: true, DateTimeOffset.UtcNow));
        var viewModel = new ProfileViewModel(messenger, presence, new StubLocalizationService());

        await viewModel.LoadAsync(7);

        Assert.False(viewModel.IsOwnProfile);
        Assert.Equal("Bob", viewModel.DisplayName);
        // Почту сервер для чужого профиля не отдаёт — строка прячется по пустому значению.
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal("Messenger_StatusOnline", viewModel.PresenceLabel);
    }

    [Fact]
    public async Task LoadAsync_Failure_ReportsError()
    {
        var messenger = new FakeMessengerService { UserDataFails = true };
        var viewModel = new ProfileViewModel(messenger, new FakeOnlinePresenceService(), new StubLocalizationService());

        await viewModel.LoadAsync(0);

        Assert.Equal("Error_ProfileLoadFailed", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }
}
