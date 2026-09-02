using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IApplicationDataStore _dataStore;
    private readonly ISettingsPreferences _settingsPreferences;
    private WindowPreferences _windowPreferences = new();
    private bool _isLoaded;

    public SettingsViewModel(IApplicationDataStore dataStore, ISettingsPreferences settingsPreferences)
    {
        _dataStore = dataStore;
        _settingsPreferences = settingsPreferences;
    }

    /// <summary>
    /// Единственный источник истины о поведении при закрытии окна: его читает
    /// <see cref="MainWindow"/> в момент закрытия.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExitsOnClose), nameof(MinimizesToTray))]
    private WindowClosingBehavior _closingBehavior = WindowClosingBehavior.Exit;

    public bool ExitsOnClose
    {
        get => ClosingBehavior == WindowClosingBehavior.Exit;
        set
        {
            if (value)
            {
                ApplyClosingBehavior(WindowClosingBehavior.Exit);
            }
        }
    }

    public bool MinimizesToTray
    {
        get => ClosingBehavior == WindowClosingBehavior.MinimizeToTray;
        set
        {
            if (value)
            {
                ApplyClosingBehavior(WindowClosingBehavior.MinimizeToTray);
            }
        }
    }

    [ObservableProperty]
    private bool _rememberWindowSize = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSizeDescription))]
    private int _windowWidth = WindowPreferences.DefaultWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSizeDescription))]
    private int _windowHeight = WindowPreferences.DefaultHeight;

    public string WindowSizeDescription => $"{WindowWidth} × {WindowHeight}";

    public event EventHandler? WindowSizeResetRequested;

    public async Task LoadAsync()
    {
        _isLoaded = false;

        var stored = await _dataStore.GetWindowClosingBehaviorAsync();
        if (stored is null)
        {
            await _dataStore.SaveWindowClosingBehaviorAsync(ClosingBehavior);
        }
        else
        {
            ClosingBehavior = stored.Value;
        }

        _windowPreferences = await _settingsPreferences.GetWindowPreferencesAsync();
        RememberWindowSize = _windowPreferences.RememberSize;
        WindowWidth = _windowPreferences.Width;
        WindowHeight = _windowPreferences.Height;
        _isLoaded = true;
    }

    public async Task SetRememberWindowSizeAsync(bool remember, CancellationToken cancellationToken = default)
    {
        if (!_isLoaded || RememberWindowSize == remember)
        {
            return;
        }

        RememberWindowSize = remember;
        await SaveWindowPreferencesAsync(_windowPreferences with { RememberSize = remember }, cancellationToken);
    }

    public async Task SaveWindowSizeAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        if (!_isLoaded || !RememberWindowSize)
        {
            return;
        }

        var preferences = _windowPreferences with { Width = width, Height = height };
        WindowWidth = preferences.Width;
        WindowHeight = preferences.Height;
        await SaveWindowPreferencesAsync(preferences, cancellationToken);
    }

    public async Task ResetWindowSizeAsync(CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
        {
            return;
        }

        var preferences = _windowPreferences with
        {
            Width = WindowPreferences.DefaultWidth,
            Height = WindowPreferences.DefaultHeight
        };

        WindowWidth = preferences.Width;
        WindowHeight = preferences.Height;
        await SaveWindowPreferencesAsync(preferences, cancellationToken);
        WindowSizeResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveWindowPreferencesAsync(WindowPreferences preferences, CancellationToken cancellationToken)
    {
        _windowPreferences = preferences;
        await _settingsPreferences.SaveWindowPreferencesAsync(preferences, cancellationToken);
    }

    private void ApplyClosingBehavior(WindowClosingBehavior behavior)
    {
        if (ClosingBehavior == behavior)
        {
            return;
        }

        ClosingBehavior = behavior;
        _ = _dataStore.SaveWindowClosingBehaviorAsync(behavior);
    }
}
