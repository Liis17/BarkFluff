using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Users;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsPrivacyViewModel(IUserPreferencesService service, ILocalizationService localization) : ObservableObject
{
    private PrivacySettings? _settings;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _profileVisibleOnSite;
    [ObservableProperty] private bool _searchVisible;
    [ObservableProperty] private int _avatarVisibility;
    [ObservableProperty] private int _bioVisibility;
    [ObservableProperty] private int _emailVisibility;
    [ObservableProperty] private int _onlineVisibility;

    public async Task LoadAsync()
    {
        IsBusy = true;
        var (error, settings) = await service.GetPrivacySettingsAsync();
        IsBusy = false;
        if (error is not null || settings is null) { ErrorMessage = localization.GetString(error ?? "Error_SettingsLoadFailed"); return; }
        Apply(settings);
    }

    public Task SetProfileVisibleAsync(bool value) => UpdateAsync(s => s.ProfileVisibleOnSite = value);
    public Task SetSearchVisibleAsync(bool value) => UpdateAsync(s => s.SearchVisible = value);
    public Task SetAvatarVisibilityAsync(int value) => UpdateAsync(s => s.AvatarVisibility = (ProfileFieldVisibility)value);
    public Task SetBioVisibilityAsync(int value) => UpdateAsync(s => s.BioVisibility = (ProfileFieldVisibility)value);
    public Task SetEmailVisibilityAsync(int value) => UpdateAsync(s => s.EmailVisibility = (ProfileFieldVisibility)value);
    public Task SetOnlineVisibilityAsync(int value) => UpdateAsync(s => s.OnlineVisibility = (ProfileFieldVisibility)value);

    private async Task UpdateAsync(Action<PrivacySettings> change)
    {
        if (_settings is null) return;
        var updated = _settings.Clone(); change(updated); Apply(updated);
        var error = await service.UpdatePrivacySettingsAsync(updated);
        if (error is null) return;
        ErrorMessage = localization.GetString(error);
        await LoadAsync();
    }

    private void Apply(PrivacySettings settings)
    {
        _settings = settings; ErrorMessage = null;
        ProfileVisibleOnSite = settings.ProfileVisibleOnSite; SearchVisible = settings.SearchVisible;
        AvatarVisibility = (int)settings.AvatarVisibility; BioVisibility = (int)settings.BioVisibility;
        EmailVisibility = (int)settings.EmailVisibility; OnlineVisibility = (int)settings.OnlineVisibility;
    }
}
