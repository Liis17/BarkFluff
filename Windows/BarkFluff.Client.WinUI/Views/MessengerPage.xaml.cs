using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    private void OnChatListContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var list = (ListView)sender;
        var hasPosition = args.TryGetPosition(list, out var position);
        var source = hasPosition ? args.OriginalSource : FocusManager.GetFocusedElement(list.XamlRoot);
        args.Handled = ShowChatListMenu(list, source, hasPosition ? position : null);
    }

    private bool ShowChatListMenu(ListView list, object? source, global::Windows.Foundation.Point? position)
    {
        ListViewItem? container = null;
        for (var element = source as DependencyObject; element is not null && element != list;
            element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem item)
            {
                container = item;
                break;
            }
        }

        if (container is null || list.ItemFromContainer(container) is not ChatItemViewModel chat)
        {
            return false;
        }

        var menu = (MenuFlyout)Resources["ChatListMenu"];
        var mute = (MenuFlyoutItem)menu.Items[0];
        mute.Text = chat.MuteMenuHeader;
        mute.Command = ViewModel.ToggleChatMuteCommand;
        mute.CommandParameter = chat;
        mute.IsEnabled = !chat.IsChangingMute;
        PopulateFolders((MenuFlyoutSubItem)menu.Items[1], chat);
        if (position is { } point)
        {
            menu.ShowAt(list, point);
        }
        else
        {
            menu.ShowAt(container);
        }
        return true;
    }

    private void PopulateFolders(MenuFlyoutSubItem submenu, ChatItemViewModel chat)
    {
        submenu.Items.Clear();
        if (ViewModel.ChatFolders is not { } state)
        {
            submenu.IsEnabled = false;
            return;
        }

        var lifetime = state.Lifetime;
        submenu.IsEnabled = !lifetime.IsCancellationRequested;
        foreach (var folder in state.Folders)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = folder.FolderName,
                IsChecked = folder.ChatList.Contains(chat.Id),
                IsEnabled = !state.IsSaving
            };
            item.Click += async (_, _) => await ViewModel.SetChatFolderAsync(chat, folder.FolderId, item.IsChecked, lifetime);
            submenu.Items.Add(item);
        }

        if (submenu.Items.Count == 0)
        {
            if (state.IsBusy || state.LoadError is not null)
            {
                submenu.Items.Add(new MenuFlyoutItem
                {
                    Text = ResourceString(state.IsBusy ? "Messenger_ChatFolders_Loading" : "Error_SettingsLoadFailed"),
                    IsEnabled = false
                });
            }
            else
            {
                var openSettings = new MenuFlyoutItem { Text = ResourceString("Messenger_ChatFolders_Empty") };
                openSettings.Click += (_, _) =>
                {
                    if (lifetime == state.Lifetime)
                    {
                        Frame.Navigate(typeof(SettingsPage), "chatfolders");
                    }
                };
                submenu.Items.Add(openSettings);
            }
        }
    }

    private static string ResourceString(string key) => (string)Application.Current.Resources[key];

    private void OnChatListMenuClosed(object sender, object args)
    {
        var menu = (MenuFlyout)sender;
        ((MenuFlyoutItem)menu.Items[0]).CommandParameter = null;
        ((MenuFlyoutSubItem)menu.Items[1]).Items.Clear();
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
