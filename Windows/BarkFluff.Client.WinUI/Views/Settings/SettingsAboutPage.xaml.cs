using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsAboutPage : Page
{
    public SettingsAboutPage() => InitializeComponent();

    public SettingsAboutViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsAboutViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
    }

    private async void OnPingClick(object sender, RoutedEventArgs e) => await ViewModel.PingBeaconAsync();
}
