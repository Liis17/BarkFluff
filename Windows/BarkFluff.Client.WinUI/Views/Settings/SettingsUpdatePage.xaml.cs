using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
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
        _ = ViewModel.CheckAsync();
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await ViewModel.CheckAsync();

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var packagePath = await ViewModel.DownloadUpdateAsync();
        if (packagePath is null)
        {
            return;
        }

        try
        {
            var packageFile = await StorageFile.GetFileFromPathAsync(packagePath);
            if (!await Launcher.LaunchFileAsync(packageFile))
            {
                ViewModel.MarkLaunchFailed();
            }
        }
        catch (Exception)
        {
            ViewModel.MarkLaunchFailed();
        }
    }

    private async void OnOpenSiteClick(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("https://barkfluff.com"));
}
