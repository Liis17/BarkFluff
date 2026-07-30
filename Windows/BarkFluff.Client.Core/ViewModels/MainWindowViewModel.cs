using CommunityToolkit.Mvvm.ComponentModel;

using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewModel;

    public MainWindowViewModel(IOnboardingNavigationService navigation, SettingsViewModel settings)
    {
        Settings = settings;
        CurrentViewModel = navigation.CurrentViewModel;
        navigation.CurrentViewModelChanged += (_, eventArgs) => CurrentViewModel = eventArgs.ViewModel;
    }

    public SettingsViewModel Settings { get; }
}
