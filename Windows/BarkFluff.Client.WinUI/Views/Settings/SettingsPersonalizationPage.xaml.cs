using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsPersonalizationPage : Page
{
    public SettingsPersonalizationPage() => InitializeComponent();

    public SettingsPersonalizationViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsPersonalizationViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
    }

    private async void OnCornerRadiusChanged(object sender, RangeBaseValueChangedEventArgs e) => await ViewModel.SetChatCornerRadiusAsync((int)Math.Round(e.NewValue));
    private async void OnBlurToggled(object sender, RoutedEventArgs e) => await ViewModel.SetChatBackgroundBlurAsync(((ToggleSwitch)sender).IsOn);
    private async void OnBlurStrengthChanged(object sender, RangeBaseValueChangedEventArgs e) => await ViewModel.SetChatBackgroundBlurRadiusAsync((int)Math.Round(e.NewValue));
    private async void OnDimChanged(object sender, RangeBaseValueChangedEventArgs e) => await ViewModel.SetChatBackgroundDimAsync((int)Math.Round(e.NewValue));
    private async void OnRelativeOnlineTimeToggled(object sender, RoutedEventArgs e) => await ViewModel.SetRelativeOnlineTimeAsync(((ToggleSwitch)sender).IsOn);
}
