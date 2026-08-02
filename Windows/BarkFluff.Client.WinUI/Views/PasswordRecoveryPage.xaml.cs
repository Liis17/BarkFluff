using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class PasswordRecoveryPage : Page
{
    public PasswordRecoveryPage() => InitializeComponent();

    public PasswordRecoveryViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (PasswordRecoveryViewModel)e.Parameter;
        Bindings.Update();
    }
}
