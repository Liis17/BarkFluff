using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface ISettingsPreferences
{
    Task<InterfacePreferences> GetInterfacePreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveInterfacePreferencesAsync(InterfacePreferences preferences, CancellationToken cancellationToken = default);
    Task<TestingPreferences> GetTestingPreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveTestingPreferencesAsync(TestingPreferences preferences, CancellationToken cancellationToken = default);
}
