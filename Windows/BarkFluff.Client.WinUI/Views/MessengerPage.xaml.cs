using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage : Page
{
    public MessengerPage() => InitializeComponent();

    public MessengerViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Страница закэширована, при возврате с профиля параметра нет — ViewModel уже присвоена.
        if (e.Parameter is MessengerViewModel viewModel)
        {
            ViewModel = viewModel;
            Bindings.Update();
        }
    }

    /// <summary>
    /// <c>ScrollViewer</c> меряет содержимое бесконечной высотой, поэтому запаса для
    /// <c>VerticalAlignment="Bottom"</c> нет. Пол задаётся по фактическому размеру вьюпорта —
    /// это и прижимает короткую переписку к нижней кромке.
    /// </summary>
    private void OnFeedScrollerSizeChanged(object sender, SizeChangedEventArgs eventArgs) =>
        FeedHost.MinHeight = eventArgs.NewSize.Height;

    private void OnChatHeaderClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel.SelectedChat?.PeerUserId is { } peerUserId)
        {
            Frame.Navigate(typeof(ProfilePage), peerUserId);
        }
    }
}
