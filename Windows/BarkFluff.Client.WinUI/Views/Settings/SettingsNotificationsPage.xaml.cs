using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsNotificationsPage : Page
{
    public SettingsNotificationsPage() => InitializeComponent();
    public SettingsNotificationsViewModel ViewModel { get; private set; } = null!;
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); ViewModel=App.Services.GetRequiredService<SettingsNotificationsViewModel>(); Bindings.Update(); }
    private async void OnToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.SetEnabledAsync(((ToggleSwitch)sender).IsOn);
}
