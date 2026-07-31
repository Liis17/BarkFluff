using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsAboutViewModel(IClientSession session, ISettingsPreferences preferencesService, IAboutSettingsService aboutService, ILocalizationService localization) : ObservableObject
{
    [ObservableProperty] private bool _isServerAddressesVisible;
    [ObservableProperty] private bool _isPinging;
    [ObservableProperty] private string? _pingResult;
    public string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    public string DeviceName => Environment.MachineName;
    public string DeviceId => session.CurrentConnection?.ConnectionParameters.DeviceId ?? string.Empty;
    public string OperatingSystem => Environment.OSVersion.VersionString;
    public ObservableCollection<SettingsAboutServiceItem> Services { get; } = [];

    public async Task LoadAsync()
    {
        IsServerAddressesVisible = (await preferencesService.GetTestingPreferencesAsync()).ShowServerAddressesInAbout;
        Services.Clear();
        var parameters = session.CurrentConnection?.ConnectionParameters;
        if (parameters is null) return;
        Services.Add(new("Beacon", parameters.SocketBeacon)); Services.Add(new("Identity", parameters.SocketIdentity));
        Services.Add(new("Users", parameters.SocketUsers)); Services.Add(new("Files", parameters.SocketFiles));
        Services.Add(new("Messages", parameters.SocketMessages)); Services.Add(new("Updates", parameters.SocketUpdates));
        Services.Add(new("Onliner", parameters.SocketOnliner));
    }

    public async Task PingBeaconAsync()
    {
        IsPinging = true; PingResult = null;
        var stopwatch = Stopwatch.StartNew(); var success = await aboutService.PingBeaconAsync(); stopwatch.Stop();
        PingResult = success ? string.Format(localization.GetString("Settings_About_PingResult"), stopwatch.ElapsedMilliseconds) : localization.GetString("Settings_About_PingFailed");
        IsPinging = false;
    }
}

public sealed record SettingsAboutServiceItem(string Name, string Address);
