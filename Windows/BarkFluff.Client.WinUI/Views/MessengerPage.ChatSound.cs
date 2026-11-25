using BarkFluff.Client.Core.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private ContentDialog? _chatSoundDialog;
    private bool _editingChatSound;

    private async void OnChatSoundClick(object sender, RoutedEventArgs args)
    {
        if (_editingChatSound)
            return;

        _editingChatSound = true;
        try
        {
            await ViewModel.EditChatSoundAsync(ShowChatSoundDialogAsync);
        }
        finally
        {
            _chatSoundDialog = null;
            _editingChatSound = false;
        }
    }

    private async Task<ChatNotificationSound?> ShowChatSoundDialogAsync(
        ChatNotificationSound current, Func<bool> canPreview, Action<ChatNotificationSound> previewSound,
        CancellationToken cancellationToken)
    {
        if (!IsLoaded || XamlRoot is null || cancellationToken.IsCancellationRequested)
            return null;

        var sounds = Enum.GetValues<ChatNotificationSound>();
        var choices = new ComboBox { Header = ResourceString("Messenger_ChatSound"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var sound in sounds)
            choices.Items.Add(ResourceString("Messenger_ChatSound" + sound));
        choices.SelectedIndex = Array.IndexOf(sounds, current);

        var preview = new Button { Content = ResourceString("Messenger_ChatSoundPreview") };
        void UpdatePreview() => preview.IsEnabled = choices.SelectedIndex >= 0 &&
            sounds[choices.SelectedIndex] != ChatNotificationSound.Silent && canPreview();
        UpdatePreview();
        choices.SelectionChanged += (_, _) => UpdatePreview();
        preview.Click += (_, _) =>
        {
            UpdatePreview();
            if (preview.IsEnabled)
                previewSound(sounds[choices.SelectedIndex]);
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = ResourceString("Messenger_ChatSoundDeviceOnly"),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(choices);
        content.Children.Add(preview);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResourceString("Messenger_ChatSound"),
            Content = content,
            PrimaryButtonText = ResourceString("Common_Save"),
            CloseButtonText = ResourceString("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        _chatSoundDialog = dialog;
        using var closeOnContextChange = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_chatSoundDialog, dialog))
                dialog.Hide();
        }));
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !cancellationToken.IsCancellationRequested &&
            choices.SelectedIndex >= 0
            ? sounds[choices.SelectedIndex] : null;
    }
}
