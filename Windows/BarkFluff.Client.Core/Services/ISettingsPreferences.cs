using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface ISettingsPreferences
{
    Task<double> GetVoicePlaybackSpeedAsync(CancellationToken cancellationToken = default);
    Task SaveVoicePlaybackSpeedAsync(double speed, CancellationToken cancellationToken = default);
    Task<InterfacePreferences> GetInterfacePreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveInterfacePreferencesAsync(InterfacePreferences preferences, CancellationToken cancellationToken = default);
    Task<TestingPreferences> GetTestingPreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveTestingPreferencesAsync(TestingPreferences preferences, CancellationToken cancellationToken = default);
    Task<WindowPreferences> GetWindowPreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveWindowPreferencesAsync(WindowPreferences preferences, CancellationToken cancellationToken = default);
}
