using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Users;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private ContentDialog? _secretDialog;

    private string SecretText(string key) => (string)Application.Current.Resources[key];

    private void OnOpenSecretChats(object sender, RoutedEventArgs e) => ViewModel.SecretChats.Open();
    private void OnCloseSecretChats(object sender, RoutedEventArgs e) => ViewModel.SecretChats.Close();

    private void OnSecretChatItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SecretChatRow row)
            ViewModel.SecretChats.SelectedChat = row;
    }

    private void OnSecretSearchMenuOpening(object sender, object e)
    {
        if (sender is MenuFlyout { Items: [MenuFlyoutItem item] })
            item.Visibility = ViewModel.SecretChats.IsAvailable &&
                item.Tag is UserSearchResultViewModel { IsRestricted: false }
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCreateSecretFromSearch(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: UserSearchResultViewModel { IsRestricted: false } user })
            await CreateSecretChatAsync(user.UserId, user.DisplayName);
    }

    private async void OnCreateSecretChat(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedChat is { PeerUserId: > 0, IsRestricted: false } chat)
            await CreateSecretChatAsync(chat.PeerUserId.Value, chat.Title);
    }

    private async Task CreateSecretChatAsync(long userId, string name)
    {
        var vm = ViewModel.SecretChats;
        if (!vm.IsAvailable || _secretDialog is not null)
            return;
        var session = vm.SessionVersion;
        var devices = await vm.LoadDevicesAsync(userId);
        if (vm.SessionVersion != session || !vm.IsAvailable || XamlRoot is null || _secretDialog is not null)
            return;

        var picker = new ComboBox
        {
            Header = SecretText("Secret_Device"), ItemsSource = devices, SelectedIndex = 0,
            DisplayMemberPath = nameof(PeerDeviceInfo.DisplayName), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var text = new TextBox { Header = SecretText("Secret_Message"), TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel { Spacing = 12, Children = { picker, text } };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = SecretText("Secret_Create"),
            CloseButtonText = SecretText("Common_Cancel"),
            Content = devices.Count == 0
                ? new TextBlock { Text = SecretText("Secret_NoDevices"), TextWrapping = TextWrapping.Wrap }
                : form
        };
        if (devices.Count != 0)
        {
            dialog.PrimaryButtonText = SecretText("Messenger_Send");
            dialog.IsPrimaryButtonEnabled = false;
            text.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(text.Text);
        }

        _secretDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && vm.SessionVersion == session &&
                vm.IsAvailable && picker.SelectedItem is PeerDeviceInfo device)
            {
                vm.Open();
                await vm.CreateAsync(userId, name, device.DeviceId, text.Text);
            }
        }
        finally
        {
            text.Text = "";
            _secretDialog = null;
        }
    }

    private async void OnAcceptSecretInvite(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecretInviteRow row })
            await ViewModel.SecretChats.AcceptCommand.ExecuteAsync(row);
    }

    private async void OnRejectSecretInvite(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecretInviteRow row })
            await ViewModel.SecretChats.RejectCommand.ExecuteAsync(row);
    }

    private async void OnDeleteFailedSecretMessage(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecretMessageRow row })
            await ViewModel.SecretChats.DeleteFailedCommand.ExecuteAsync(row);
    }

    private async void OnForgetSecretChat(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SecretChats;
        if (vm.SelectedChat is not { } chat || _secretDialog is not null)
            return;
        var session = vm.SessionVersion;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = SecretText("Secret_Forget"), Content = SecretText("Secret_ForgetConfirm"),
            PrimaryButtonText = SecretText("Common_Delete"), CloseButtonText = SecretText("Common_Cancel")
        };
        _secretDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && vm.SessionVersion == session)
                await vm.ForgetAsync(chat);
        }
        finally
        {
            _secretDialog = null;
        }
    }
}
