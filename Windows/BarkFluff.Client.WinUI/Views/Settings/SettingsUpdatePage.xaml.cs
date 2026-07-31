using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsUpdatePage : Page
{
    public SettingsUpdatePage() => InitializeComponent();

    public SettingsUpdateViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsUpdateViewModel>();
        Bindings.Update();
    }

    private async void OnReleaseCheckClick(object sender, RoutedEventArgs e) => await ViewModel.CheckAsync(UpdateChannel.Release);
    private async void OnBetaCheckClick(object sender, RoutedEventArgs e) => await ViewModel.CheckAsync(UpdateChannel.Beta);
    private async void OnDownloadClick(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri(ViewModel.DownloadUrl!));
    private async void OnOpenSiteClick(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("https://barkfluff.com"));
}
