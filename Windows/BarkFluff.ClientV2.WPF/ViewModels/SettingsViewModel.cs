using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IApplicationDataStore _dataStore;

    public SettingsViewModel(IApplicationDataStore dataStore)
    {
        _dataStore = dataStore;
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

    public async Task LoadAsync()
    {
        var stored = await _dataStore.GetWindowClosingBehaviorAsync();
        if (stored is null)
        {
            await _dataStore.SaveWindowClosingBehaviorAsync(ClosingBehavior);
            return;
        }

        ClosingBehavior = stored.Value;
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
