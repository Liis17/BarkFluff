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
    [NotifyPropertyChangedFor(nameof(WindowSizeDescription), nameof(WindowBoundsDescription))]
    private int _windowWidth = WindowPreferences.DefaultWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSizeDescription), nameof(WindowBoundsDescription))]
    private int _windowHeight = WindowPreferences.DefaultHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowBoundsDescription))]
    private int? _windowPositionX;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowBoundsDescription))]
    private int? _windowPositionY;

    public string WindowSizeDescription => $"{WindowWidth} × {WindowHeight}";
    public string WindowBoundsDescription =>
        WindowPositionX is int x && WindowPositionY is int y
            ? $"{WindowSizeDescription} (X: {x}, Y: {y})"
            : WindowSizeDescription;

    public event EventHandler? WindowBoundsResetRequested;

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
        WindowPositionX = _windowPreferences.PositionX;
        WindowPositionY = _windowPreferences.PositionY;
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

    public async Task SaveWindowBoundsAsync(
        int width,
        int height,
        int positionX,
        int positionY,
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded || !RememberWindowSize)
        {
            return;
        }

        var preferences = _windowPreferences with
        {
            Width = width > 0 ? width : WindowPreferences.DefaultWidth,
            Height = height > 0 ? height : WindowPreferences.DefaultHeight,
            PositionX = positionX,
            PositionY = positionY
        };

        WindowWidth = preferences.Width;
        WindowHeight = preferences.Height;
        WindowPositionX = preferences.PositionX;
        WindowPositionY = preferences.PositionY;
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
            Height = WindowPreferences.DefaultHeight,
            PositionX = null,
            PositionY = null
        };

        WindowWidth = preferences.Width;
        WindowHeight = preferences.Height;
        WindowPositionX = preferences.PositionX;
        WindowPositionY = preferences.PositionY;
        await SaveWindowPreferencesAsync(preferences, cancellationToken);
        WindowBoundsResetRequested?.Invoke(this, EventArgs.Empty);
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
