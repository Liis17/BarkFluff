using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsUpdateViewModel(IUpdateService updateService, ILocalizationService localization) : ObservableObject
{
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isUpdateAvailable;

    public string CurrentVersion => UpdateVersion.Format(
        UpdateVersion.Normalize(Assembly.GetEntryAssembly()?.GetName().Version)) ?? "0.0.0";

    public string CurrentChannelName => updateService.CurrentChannel switch
    {
        UpdateChannel.Release => localization.GetString("Settings_Update_ChannelRelease"),
        UpdateChannel.Dev => localization.GetString("Settings_Update_ChannelDev"),
        UpdateChannel.Nightly => localization.GetString("Settings_Update_ChannelNightly"),
        _ => localization.GetString("Settings_Update_ChannelUnavailable")
    };

    public bool IsChannelAvailable => updateService.CurrentChannel is not null;
    public bool CanCheck => IsChannelAvailable && !IsChecking && !IsDownloading;
    public bool CanDownload => IsUpdateAvailable && !IsChecking && !IsDownloading;

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
    }

    partial void OnIsUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(CanDownload));

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsChannelAvailable)
        {
            IsUpdateAvailable = false;
            StatusMessage = localization.GetString("Settings_Update_ChannelUnavailable");
            return;
        }

        IsChecking = true;
        IsUpdateAvailable = false;
        StatusMessage = localization.GetString("Settings_Update_Checking");
        try
        {
            var result = await updateService.CheckForUpdatesAsync(cancellationToken);
            IsUpdateAvailable = result.IsUpdateAvailable;
            StatusMessage = result.IsUpdateAvailable ? string.Format(localization.GetString("Settings_Update_Available"), result.LatestVersion) : localization.GetString("Settings_Update_UpToDate");
        }
        catch (HttpRequestException) { StatusMessage = localization.GetString("Error_SettingsLoadFailed"); }
        catch (System.Text.Json.JsonException) { StatusMessage = localization.GetString("Error_SettingsLoadFailed"); }
        finally { IsChecking = false; }
    }

    public async Task<string?> DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownload)
        {
            return null;
        }

        IsDownloading = true;
        StatusMessage = localization.GetString("Settings_Update_Downloading");
        try
        {
            var packagePath = await updateService.DownloadUpdateAsync(cancellationToken);
            if (packagePath is null)
            {
                IsUpdateAvailable = false;
                StatusMessage = localization.GetString("Settings_Update_UpToDate");
                return null;
            }

            StatusMessage = localization.GetString("Settings_Update_Launched");
            return packagePath;
        }
        catch (HttpRequestException)
        {
            StatusMessage = localization.GetString("Settings_Update_DownloadFailed");
            return null;
        }
        catch (InvalidDataException)
        {
            StatusMessage = localization.GetString("Settings_Update_DownloadFailed");
            return null;
        }
        catch (IOException)
        {
            StatusMessage = localization.GetString("Settings_Update_DownloadFailed");
            return null;
        }
        finally { IsDownloading = false; }
    }

    public void MarkLaunchFailed() => StatusMessage = localization.GetString("Settings_Update_LaunchFailed");
}
