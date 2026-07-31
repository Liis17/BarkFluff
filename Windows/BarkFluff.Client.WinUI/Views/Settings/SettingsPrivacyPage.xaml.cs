using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsPrivacyPage : Page
{
    private bool _isLoading;
    public SettingsPrivacyPage() => InitializeComponent();
    public SettingsPrivacyViewModel ViewModel { get; private set; } = null!;
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _isLoading = true; ViewModel = App.Services.GetRequiredService<SettingsPrivacyViewModel>(); Bindings.Update(); await ViewModel.LoadAsync(); _isLoading = false; }
    private async void OnProfileVisibleToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { if (!_isLoading) await ViewModel.SetProfileVisibleAsync(((ToggleSwitch)sender).IsOn); }
    private async void OnSearchVisibleToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { if (!_isLoading) await ViewModel.SetSearchVisibleAsync(((ToggleSwitch)sender).IsOn); }
    private async void OnAvatarVisibilityChanged(object sender, SelectionChangedEventArgs e) { if (!_isLoading) await ViewModel.SetAvatarVisibilityAsync(((ComboBox)sender).SelectedIndex); }
    private async void OnBioVisibilityChanged(object sender, SelectionChangedEventArgs e) { if (!_isLoading) await ViewModel.SetBioVisibilityAsync(((ComboBox)sender).SelectedIndex); }
    private async void OnEmailVisibilityChanged(object sender, SelectionChangedEventArgs e) { if (!_isLoading) await ViewModel.SetEmailVisibilityAsync(((ComboBox)sender).SelectedIndex); }
    private async void OnOnlineVisibilityChanged(object sender, SelectionChangedEventArgs e) { if (!_isLoading) await ViewModel.SetOnlineVisibilityAsync(((ComboBox)sender).SelectedIndex); }
}
