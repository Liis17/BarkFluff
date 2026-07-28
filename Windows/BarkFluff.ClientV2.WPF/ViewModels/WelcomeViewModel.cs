using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IApplicationDataStore _dataStore;
    private readonly IOnboardingNavigationService _navigation;

    public WelcomeViewModel(IApplicationDataStore dataStore, IOnboardingNavigationService navigation)
    {
        _dataStore = dataStore;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        await _dataStore.MarkWelcomeSeenAsync();
        _navigation.ShowSelectNode();
    }
}
