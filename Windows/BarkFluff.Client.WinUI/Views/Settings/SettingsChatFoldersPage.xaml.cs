using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsChatFoldersPage : Page
{
    private ContentDialog? _membershipDialog;

    public SettingsChatFoldersPage() => InitializeComponent();

    public SettingsChatFoldersViewModel ViewModel { get; } =
        App.Services.GetRequiredService<SettingsChatFoldersViewModel>();

    private async void OnPageLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _membershipDialog?.Hide();
        ViewModel.Deactivate();
    }

    private async void OnManageChatsClick(object sender, RoutedEventArgs e)
    {
        if (_membershipDialog is not null || ((FrameworkElement)sender).DataContext is not SettingsChatFolderItem item ||
            ViewModel.CreateSelection(item) is not { } selection)
        {
            return;
        }

        var search = new TextBox { Header = ResourceString("Settings_ChatFolders_SearchChats") };
        var choices = new StackPanel();
        var empty = new TextBlock { Text = ResourceString("Settings_ChatFolders_NoChats") };
        choices.Children.Add(empty);
        var checkboxes = selection.Chats.ToDictionary(chat => chat, chat =>
        {
            var checkbox = new CheckBox { Content = chat.Title, IsChecked = selection.Includes(chat) };
            checkbox.Checked += (_, _) => selection.SetIncluded(chat, true);
            checkbox.Unchecked += (_, _) => selection.SetIncluded(chat, false);
            choices.Children.Add(checkbox);
            return checkbox;
        });

        void Filter()
        {
            var visible = selection.Search(search.Text).ToHashSet();
            foreach (var (chat, checkbox) in checkboxes)
            {
                checkbox.Visibility = visible.Contains(chat) ? Visibility.Visible : Visibility.Collapsed;
            }
            empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        search.TextChanged += (_, _) => Filter();
        Filter();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResourceString("Settings_ChatFolders_ManageChats"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    search,
                    new ScrollViewer { MaxHeight = 360, Content = choices }
                }
            },
            PrimaryButtonText = ResourceString("Common_Save"),
            CloseButtonText = ResourceString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = selection.Chats.Count > 0
        };
        _membershipDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.ApplySelectionAsync(selection);
            }
        }
        finally
        {
            _membershipDialog = null;
        }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await EditFolderAsync();

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SettingsChatFolderItem item)
        {
            await EditFolderAsync(item);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SettingsChatFolderItem item)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResourceString("Settings_ChatFolders_Delete"),
            Content = ResourceString("Settings_ChatFolders_DeleteConfirm"),
            PrimaryButtonText = ResourceString("Common_Delete"),
            CloseButtonText = ResourceString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        var lifetime = ViewModel.Lifetime;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && lifetime == ViewModel.Lifetime)
        {
            await ViewModel.DeleteAsync(item);
        }
    }

    private async void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SettingsChatFolderItem item)
        {
            await ViewModel.MoveAsync(item, -1);
        }
    }

    private async void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SettingsChatFolderItem item)
        {
            await ViewModel.MoveAsync(item, 1);
        }
    }

    private async Task EditFolderAsync(SettingsChatFolderItem? item = null)
    {
        var nameBox = new TextBox { Header = ResourceString("Settings_ChatFolders_Name"), Text = item?.Name ?? string.Empty };
        var iconBox = new TextBox { Header = ResourceString("Settings_ChatFolders_Icon"), Text = item?.Icon ?? string.Empty };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResourceString("Settings_ChatFolders_EditTitle"),
            Content = new StackPanel { Spacing = 12, Children = { nameBox, iconBox } },
            PrimaryButtonText = ResourceString("Common_Save"),
            CloseButtonText = ResourceString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var lifetime = ViewModel.Lifetime;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || lifetime != ViewModel.Lifetime)
        {
            return;
        }

        if (item is null)
        {
            await ViewModel.CreateAsync(nameBox.Text, iconBox.Text);
        }
        else
        {
            await ViewModel.UpdateAsync(item, nameBox.Text, iconBox.Text);
        }
    }

    public static string ManageChatsAutomationName(string name) =>
        $"{ResourceString("Settings_ChatFolders_ManageChats")} — {name}";

    private static string ResourceString(string key) => (string)Application.Current.Resources[key];
}
