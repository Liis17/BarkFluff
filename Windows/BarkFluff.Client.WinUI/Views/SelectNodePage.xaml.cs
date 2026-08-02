using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class SelectNodePage : Page
{
    public SelectNodePage() => InitializeComponent();

    public SelectNodeViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (SelectNodeViewModel)e.Parameter;
        Bindings.Update();
    }
}
