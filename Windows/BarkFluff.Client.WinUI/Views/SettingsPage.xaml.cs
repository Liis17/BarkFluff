using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    public SettingsViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (SettingsViewModel)e.Parameter;
        Bindings.Update();
    }
}
