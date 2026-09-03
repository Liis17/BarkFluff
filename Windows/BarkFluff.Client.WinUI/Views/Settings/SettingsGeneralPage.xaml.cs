using BarkFluff.Client.Core.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

/// <summary>
/// Раздел без аналога в Android: поведение при закрытии окна специфично для настольного клиента.
/// </summary>
public sealed partial class SettingsGeneralPage : Page
{
    public SettingsGeneralPage() => InitializeComponent();

    public SettingsViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Bindings.Update();
    }

    private async void OnRememberWindowSizeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            await ViewModel.SetRememberWindowSizeAsync(toggleSwitch.IsOn);
        }
    }

    private async void OnResetWindowSizeClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ResetWindowSizeAsync();
}
