using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsDevicesPage : Page
{
    public SettingsDevicesPage() => InitializeComponent();
    public SettingsDevicesViewModel ViewModel { get; private set; } = null!;
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); ViewModel = App.Services.GetRequiredService<SettingsDevicesViewModel>(); Bindings.Update(); await ViewModel.LoadAsync(); }
    private async void OnRemoveClick(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is DeviceSessionItem item) await ViewModel.RemoveAsync(item); }
}
