using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class ConnectedNodePage : Page
{
    public ConnectedNodePage() => InitializeComponent();

    public ConnectedNodeViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (ConnectedNodeViewModel)e.Parameter;
        Bindings.Update();
    }
}
