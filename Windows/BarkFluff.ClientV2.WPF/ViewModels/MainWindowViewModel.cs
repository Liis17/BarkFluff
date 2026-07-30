using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BarkFluff.ClientV2.WPF.Services;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private bool _isSettingsVisible;

    public MainWindowViewModel(IOnboardingNavigationService navigation, SettingsViewModel settings)
    {
        Settings = settings;
        CurrentViewModel = navigation.CurrentViewModel;
        navigation.CurrentViewModelChanged += (_, eventArgs) => CurrentViewModel = eventArgs.ViewModel;
    }

    public SettingsViewModel Settings { get; }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsVisible = !IsSettingsVisible;

    [RelayCommand]
    private void CloseSettings() => IsSettingsVisible = false;
}
