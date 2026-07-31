using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsPersonalizationViewModel(ISettingsPreferences preferencesService) : ObservableObject
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private InterfacePreferences _preferences = new();

    [ObservableProperty]
    private int _chatCornerRadius;

    [ObservableProperty]
    private bool _chatBackgroundBlur;

    [ObservableProperty]
    private int _chatBackgroundBlurRadius;

    [ObservableProperty]
    private int _chatBackgroundDim;

    [ObservableProperty]
    private bool _relativeOnlineTime;

    [ObservableProperty]
    private bool _isLoaded;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoaded = false;
        _preferences = await preferencesService.GetInterfacePreferencesAsync(cancellationToken);
        ChatCornerRadius = _preferences.ChatCornerRadius;
        ChatBackgroundBlur = _preferences.ChatBackgroundBlur;
        ChatBackgroundBlurRadius = _preferences.ChatBackgroundBlurRadius;
        ChatBackgroundDim = _preferences.ChatBackgroundDim;
        RelativeOnlineTime = _preferences.RelativeOnlineTime;
        IsLoaded = true;
    }

    public Task SetChatCornerRadiusAsync(int value, CancellationToken cancellationToken = default) =>
        SaveAsync(_preferences with { ChatCornerRadius = value }, cancellationToken);

    public Task SetChatBackgroundBlurAsync(bool value, CancellationToken cancellationToken = default) =>
        SaveAsync(_preferences with { ChatBackgroundBlur = value }, cancellationToken);

    public Task SetChatBackgroundBlurRadiusAsync(int value, CancellationToken cancellationToken = default) =>
        SaveAsync(_preferences with { ChatBackgroundBlurRadius = value }, cancellationToken);

    public Task SetChatBackgroundDimAsync(int value, CancellationToken cancellationToken = default) =>
        SaveAsync(_preferences with { ChatBackgroundDim = value }, cancellationToken);

    public Task SetRelativeOnlineTimeAsync(bool value, CancellationToken cancellationToken = default) =>
        SaveAsync(_preferences with { RelativeOnlineTime = value }, cancellationToken);

    private async Task SaveAsync(InterfacePreferences preferences, CancellationToken cancellationToken)
    {
        if (!IsLoaded)
        {
            return;
        }

        _preferences = preferences;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await preferencesService.SaveInterfacePreferencesAsync(_preferences, cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
