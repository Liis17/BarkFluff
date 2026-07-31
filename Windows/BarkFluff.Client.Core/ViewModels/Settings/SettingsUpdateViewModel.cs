using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsUpdateViewModel(IUpdateService updateService, ILocalizationService localization) : ObservableObject
{
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string? _downloadUrl;
    public string CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    public bool IsUpdateAvailable => !string.IsNullOrWhiteSpace(DownloadUrl);
    partial void OnDownloadUrlChanged(string? value) => OnPropertyChanged(nameof(IsUpdateAvailable));

    public async Task CheckAsync(UpdateChannel channel)
    {
        IsChecking = true;
        DownloadUrl = null;
        StatusMessage = localization.GetString("Settings_Update_Checking");
        try
        {
            var result = await updateService.CheckForUpdatesAsync(channel);
            DownloadUrl = result.IsUpdateAvailable ? result.DownloadUrl : null;
            StatusMessage = result.IsUpdateAvailable ? string.Format(localization.GetString("Settings_Update_Available"), result.LatestVersion) : localization.GetString("Settings_Update_UpToDate");
        }
        catch (HttpRequestException) { StatusMessage = localization.GetString("Error_SettingsLoadFailed"); }
        finally { IsChecking = false; }
    }
}
