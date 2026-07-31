using BarkFluff.Client.Core.Models;

using System.Text.Json;

namespace BarkFluff.Client.Core.Services;

public sealed class SettingsPreferences(IApplicationDataStore dataStore) : ISettingsPreferences
{
    private const string InterfacePreferencesKey = "settings.interface";
    private const string TestingPreferencesKey = "settings.testing";

    public async Task<InterfacePreferences> GetInterfacePreferencesAsync(CancellationToken cancellationToken = default) =>
        Clamp(await GetAsync<InterfacePreferences>(InterfacePreferencesKey, cancellationToken) ?? new InterfacePreferences());

    public Task SaveInterfacePreferencesAsync(InterfacePreferences preferences, CancellationToken cancellationToken = default) =>
        SaveAsync(InterfacePreferencesKey, Clamp(preferences), cancellationToken);

    public async Task<TestingPreferences> GetTestingPreferencesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<TestingPreferences>(TestingPreferencesKey, cancellationToken) ?? new TestingPreferences();

    public Task SaveTestingPreferencesAsync(TestingPreferences preferences, CancellationToken cancellationToken = default) =>
        SaveAsync(TestingPreferencesKey, preferences, cancellationToken);

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var json = await dataStore.GetPreferenceAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private Task SaveAsync<T>(string key, T preferences, CancellationToken cancellationToken) =>
        dataStore.SavePreferenceAsync(key, JsonSerializer.Serialize(preferences), cancellationToken);

    private static InterfacePreferences Clamp(InterfacePreferences preferences) => preferences with
    {
        ChatCornerRadius = Math.Clamp(preferences.ChatCornerRadius, 0, 30),
        ChatBackgroundBlurRadius = Math.Clamp(preferences.ChatBackgroundBlurRadius, 1, 25),
        ChatBackgroundDim = Math.Clamp(preferences.ChatBackgroundDim, 0, 100),
        ChatStickerSizeDp = Math.Clamp(preferences.ChatStickerSizeDp, 96, 240)
    };
}
