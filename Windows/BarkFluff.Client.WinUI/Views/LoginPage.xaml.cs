using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage() => InitializeComponent();

    public LoginViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (LoginViewModel)e.Parameter;
        Bindings.Update();
    }
}
