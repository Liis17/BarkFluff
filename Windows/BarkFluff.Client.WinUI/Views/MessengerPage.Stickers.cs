using BarkFluff.Client.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using BarkFluff.Client.WinUI.Infrastructure.Converters;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private static readonly Brush TransparentStickerBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private static Brush? MessageBackground(bool isMine, bool isStickerOnly)
    {
        if (isStickerOnly)
            return TransparentStickerBackground;

        var converter = (BoolToBrushConverter)Application.Current.Resources["MessageBubbleBrushConverter"];
        return isMine ? converter.TrueValue : converter.FalseValue;
    }

    private bool CanPickSticker(bool canAttach, bool attachmentsSending, bool stickerSending) =>
        canAttach && !attachmentsSending && !stickerSending;

    private async void OnStickerFlyoutOpened(object sender, object args) => await ViewModel.OpenStickerPickerAsync();

    private void OnStickerFlyoutClosed(object sender, object args) => ViewModel.CloseStickerPicker();

    private async void OnStickerCoverLoaded(object sender, RoutedEventArgs args)
    {
        if (ViewModel.StickerPicker is { } picker && sender is FrameworkElement { DataContext: StickerTab tab })
            await picker.LoadCoverAsync(tab);
    }

    private async void OnStickerRetryClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.StickerPicker is { } picker)
            await picker.LoadAsync();
    }

    private async void OnStickerClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is PickerSticker sticker && ViewModel.StickerPicker?.CanChoose(sticker) == true)
        {
            var sending = ViewModel.SendStickerAsync(sticker);
            StickerFlyout.Hide();
            await sending;
        }
    }
}
