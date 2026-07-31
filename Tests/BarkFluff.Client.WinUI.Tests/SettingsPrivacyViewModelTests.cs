using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsPrivacyViewModelTests
{
    [Fact]
    public async Task ChangingSetting_UpdatesOptimistically()
    {
        var service = new FakePreferencesService();
        var viewModel = new SettingsPrivacyViewModel(service, new StubLocalizationService());
        await viewModel.LoadAsync();

        await viewModel.SetSearchVisibleAsync(false);

        Assert.False(viewModel.SearchVisible);
        Assert.False(service.LastSaved!.SearchVisible);
    }

    [Fact]
    public async Task FailedSave_ReloadsTheServerState()
    {
        var service = new FakePreferencesService { SaveError = "Error_SettingsSaveFailed", ReloadedSearchVisible = false };
        var viewModel = new SettingsPrivacyViewModel(service, new StubLocalizationService());
        await viewModel.LoadAsync();

        await viewModel.SetSearchVisibleAsync(false);

        Assert.False(viewModel.SearchVisible);
        Assert.Equal(2, service.LoadCalls);
    }

    private sealed class FakePreferencesService : IUserPreferencesService
    {
        public string? SaveError { get; set; }
        public bool ReloadedSearchVisible { get; set; } = true;
        public int LoadCalls { get; private set; }
        public PrivacySettings? LastSaved { get; private set; }

        public Task<(string? ErrorKey, PrivacySettings? Settings)> GetPrivacySettingsAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult<(string?, PrivacySettings?)>((null, new PrivacySettings { SearchVisible = LoadCalls == 1 || ReloadedSearchVisible }));
        }

        public Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            return Task.FromResult(SaveError);
        }
    }
}
