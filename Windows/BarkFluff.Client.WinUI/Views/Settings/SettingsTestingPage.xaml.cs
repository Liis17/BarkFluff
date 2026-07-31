using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsTestingPage : Page
{
    public SettingsTestingPage() => InitializeComponent();

    public SettingsTestingViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsTestingViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
    }

    private async void OnShowIdsToggled(object sender, RoutedEventArgs e) => await ViewModel.SetShowIdsInProfileAsync(((ToggleSwitch)sender).IsOn);
    private async void OnShowServerAddressesToggled(object sender, RoutedEventArgs e) => await ViewModel.SetShowServerAddressesInAboutAsync(((ToggleSwitch)sender).IsOn);
    private async void OnSecretChatsToggled(object sender, RoutedEventArgs e) => await ViewModel.SetSecretChatsEnabledAsync(((ToggleSwitch)sender).IsOn);
    private async void OnPrivateChatsToggled(object sender, RoutedEventArgs e) => await ViewModel.SetPrivateChatsEnabledAsync(((ToggleSwitch)sender).IsOn);
}
