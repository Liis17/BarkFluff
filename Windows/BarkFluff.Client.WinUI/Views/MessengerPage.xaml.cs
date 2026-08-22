using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    private async void OnSearchResultItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        if (eventArgs.ClickedItem is UserSearchResultViewModel result)
        {
            await ViewModel.OpenSearchResultAsync(result);
        }
    }

    /// <summary>
    /// Системным и приватным сообщениям действия недоступны. В WinUI нет аналога
    /// <c>ContextMenuService.IsEnabled</c>, поэтому меню гасится отменой самого запроса —
    /// иначе на таком сообщении открывалось бы меню со всеми скрытыми пунктами.
    /// </summary>
    private void OnBubbleContextRequested(UIElement sender, ContextRequestedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { DataContext: MessageItemViewModel { CanUseActions: false } })
        {
            eventArgs.Handled = true;
        }
    }

    /// <summary>
    /// IsChecked вкладок читается OneWay через конвертер; индекс пишется отсюда, а не через
    /// ConvertBack — RadioButton сам снимает отметку с соседей по GroupName, и TwoWay-конвертер
    /// с UnsetValue на false-переходе ненадёжно взаимодействовал бы с этим внутренним снятием.
    /// </summary>
    private void OnProfileAttachmentTabChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is RadioButton { Tag: string tagText } && int.TryParse(tagText, out var index))
        {
            ViewModel.Profile.SelectedAttachmentTabIndex = index;
        }
    }
}
