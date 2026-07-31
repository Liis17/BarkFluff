using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsChatFoldersPage : Page
{
    public SettingsChatFoldersPage() => InitializeComponent();

    public SettingsChatFoldersViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsChatFoldersViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
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

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
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

    private static string ResourceString(string key) => (string)Application.Current.Resources[key];
}
