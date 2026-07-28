using CommunityToolkit.Mvvm.ComponentModel;

using BarkFluff.ClientV2.WPF.Services;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewModel;

    public MainWindowViewModel(IOnboardingNavigationService navigation)
    {
        CurrentViewModel = navigation.CurrentViewModel;
        navigation.CurrentViewModelChanged += (_, eventArgs) => CurrentViewModel = eventArgs.ViewModel;
    }
}
