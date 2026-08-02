using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsAccountViewModelTests
{
    [Fact]
    public async Task LoadAsync_FillsTheEditableFields()
    {
        var service = new FakeAccountSettingsService
        {
            Profile = new AccountProfile("Ada", "Lovelace", "ada", "About me", "https://node/avatar.png")
        };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync();

        Assert.Equal("Ada", viewModel.FirstName);
        Assert.Equal("Lovelace", viewModel.LastName);
        Assert.Equal("ada", viewModel.Username);
        Assert.Equal("About me", viewModel.Bio);
        Assert.Equal("https://node/avatar.png", viewModel.AvatarUrl);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenTheServerFails_ShowsAnError()
    {
        var service = new FakeAccountSettingsService { ProfileErrorKey = "Error_SettingsLoadFailed" };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync();

        Assert.Equal("Error_SettingsLoadFailed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveName_SendsBothPartsInOneCall()
    {
        var service = new FakeAccountSettingsService();
        var viewModel = CreateViewModel(service);
        viewModel.FirstName = "Ada";
        viewModel.LastName = "Lovelace";

        await viewModel.SaveNameCommand.ExecuteAsync(null);

        Assert.Equal(("Ada", "Lovelace"), service.LastSubmittedName);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveUsername_WhenTakenSurfacesTheReason()
    {
        var service = new FakeAccountSettingsService { UsernameErrorKey = "Error_UsernameTaken" };
        var viewModel = CreateViewModel(service);
        viewModel.Username = "taken";

        await viewModel.SaveUsernameCommand.ExecuteAsync(null);

        Assert.Equal("Error_UsernameTaken", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task UploadAvatar_ReloadsTheProfileSoThePreviewFollows()
    {
        var service = new FakeAccountSettingsService
        {
            Profile = new AccountProfile(string.Empty, string.Empty, string.Empty, string.Empty, "new-url")
        };
        var viewModel = CreateViewModel(service);

        await viewModel.UploadAvatarAsync(@"C:\pictures\avatar.png");

        Assert.Equal(@"C:\pictures\avatar.png", service.LastAvatarPath);
        Assert.Equal("new-url", viewModel.AvatarUrl);
    }

    [Fact]
    public void RequestingLogout_AsksForConfirmationInsteadOfLeaving()
    {
        var service = new FakeAccountSettingsService();
        var viewModel = CreateViewModel(service);

        viewModel.RequestLogoutCommand.Execute(null);

        Assert.True(viewModel.IsLogoutConfirmVisible);
        Assert.False(viewModel.IsLogoutIdle);
        Assert.Equal(0, service.LogoutCalls);
    }

    [Fact]
    public void CancellingLogout_LeavesTheSessionAlone()
    {
        var service = new FakeAccountSettingsService();
        var viewModel = CreateViewModel(service);

        viewModel.RequestLogoutCommand.Execute(null);
        viewModel.CancelLogoutCommand.Execute(null);

        Assert.False(viewModel.IsLogoutConfirmVisible);
        Assert.Equal(0, service.LogoutCalls);
    }

    [Fact]
    public async Task ConfirmingLogout_DelegatesTheWholeScenarioToTheService()
    {
        var service = new FakeAccountSettingsService { LogoutErrorKey = "Error_LogoutFailed" };
        var viewModel = CreateViewModel(service);

        viewModel.RequestLogoutCommand.Execute(null);
        await viewModel.ConfirmLogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, service.LogoutCalls);
        Assert.False(viewModel.IsLogoutConfirmVisible);
    }

    private static SettingsAccountViewModel CreateViewModel(
        FakeAccountSettingsService service) =>
        new(service, new StubLocalizationService());
}

internal sealed class FakeAccountSettingsService : IAccountSettingsService
{
    public AccountProfile? Profile { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public string? ProfileErrorKey { get; set; }

    public string? UsernameErrorKey { get; set; }

    public string? LogoutErrorKey { get; set; }

    public (string First, string Last)? LastSubmittedName { get; private set; }

    public string? LastAvatarPath { get; private set; }

    public int LogoutCalls { get; private set; }

    public Task<(string? ErrorKey, AccountProfile? Profile)> GetProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((ProfileErrorKey, ProfileErrorKey is null ? Profile : null));

    public Task<string?> ChangeNameAsync(string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        LastSubmittedName = (firstName, lastName);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ChangeUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(UsernameErrorKey);

    public Task<string?> ChangeBioAsync(string bio, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> UploadAvatarAsync(string filePath, CancellationToken cancellationToken = default)
    {
        LastAvatarPath = filePath;
        return Task.FromResult<string?>(null);
    }

    public Task<string?> LogoutAsync(CancellationToken cancellationToken = default)
    {
        LogoutCalls++;
        return Task.FromResult(LogoutErrorKey);
    }
}

internal sealed class FakeOnboardingNavigationService : IOnboardingNavigationService
{
    public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

    public object? CurrentViewModel { get; private set; }

    public int ShowLoginCalls { get; private set; }

    public void ShowWelcome() => CurrentViewModel = nameof(ShowWelcome);

    public void ShowSelectNode() => CurrentViewModel = nameof(ShowSelectNode);

    public void ShowConnectedNode() => CurrentViewModel = nameof(ShowConnectedNode);

    public void ShowLogin()
    {
        ShowLoginCalls++;
        CurrentViewModel = nameof(ShowLogin);
        CurrentViewModelChanged?.Invoke(this, new OnboardingNavigationEventArgs(CurrentViewModel));
    }

    public void ShowRegistration() => CurrentViewModel = nameof(ShowRegistration);

    public void ShowPasswordRecovery() => CurrentViewModel = nameof(ShowPasswordRecovery);
}
