using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class WelcomePage : Page
{
    public WelcomePage() => InitializeComponent();

    /// <summary>
    /// Заполняется в <see cref="OnNavigatedTo"/>: страница создаётся навигацией без параметров,
    /// поэтому к моменту <c>InitializeComponent</c> модели ещё нет.
    /// </summary>
    public WelcomeViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (WelcomeViewModel)e.Parameter;
        Bindings.Update();
    }
}
