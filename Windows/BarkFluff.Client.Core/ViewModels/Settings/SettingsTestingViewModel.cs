using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsTestingViewModel(ISettingsPreferences preferencesService) : ObservableObject
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private TestingPreferences _preferences = new();
    [ObservableProperty] private bool _showIdsInProfile;
    [ObservableProperty] private bool _showServerAddressesInAbout;
    [ObservableProperty] private bool _secretChatsEnabled;
    [ObservableProperty] private bool _privateChatsEnabled;
    [ObservableProperty] private bool _isLoaded;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoaded = false;
        _preferences = await preferencesService.GetTestingPreferencesAsync(cancellationToken);
        ShowIdsInProfile = _preferences.ShowIdsInProfile;
        ShowServerAddressesInAbout = _preferences.ShowServerAddressesInAbout;
        SecretChatsEnabled = _preferences.SecretChatsEnabled;
        PrivateChatsEnabled = _preferences.PrivateChatsEnabled;
        IsLoaded = true;
    }

    public Task SetShowIdsInProfileAsync(bool value, CancellationToken cancellationToken = default) => SaveAsync(_preferences with { ShowIdsInProfile = value }, cancellationToken);
    public Task SetShowServerAddressesInAboutAsync(bool value, CancellationToken cancellationToken = default) => SaveAsync(_preferences with { ShowServerAddressesInAbout = value }, cancellationToken);
    public Task SetSecretChatsEnabledAsync(bool value, CancellationToken cancellationToken = default) => SaveAsync(_preferences with { SecretChatsEnabled = value }, cancellationToken);
    public Task SetPrivateChatsEnabledAsync(bool value, CancellationToken cancellationToken = default) => SaveAsync(_preferences with { PrivateChatsEnabled = value }, cancellationToken);

    private async Task SaveAsync(TestingPreferences preferences, CancellationToken cancellationToken)
    {
        if (!IsLoaded) return;
        _preferences = preferences;
        await _saveLock.WaitAsync(cancellationToken);
        try { await preferencesService.SaveTestingPreferencesAsync(_preferences, cancellationToken); }
        finally { _saveLock.Release(); }
    }
}
