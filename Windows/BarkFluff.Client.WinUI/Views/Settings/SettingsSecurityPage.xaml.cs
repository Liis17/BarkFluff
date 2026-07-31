using BarkFluff.Client.Core.ViewModels.Settings;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsSecurityPage : Page
{
    public SettingsSecurityPage() => InitializeComponent();

    public SettingsSecurityViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsSecurityViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
    }
}
