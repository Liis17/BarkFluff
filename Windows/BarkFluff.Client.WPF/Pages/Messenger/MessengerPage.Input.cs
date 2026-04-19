using System.Windows;
using System.Windows.Input;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// XAML event handlers ввода: отправка, стикеры, keyboard hooks, закрытие чата.
    /// Тонкие делегирующие обёртки поверх контроллеров.
    /// </summary>
    public partial class MessengerPage
    {
        private void SendMessage(object sender, RoutedEventArgs e)
            => _send.SendTextMessage();

        private void StickerButton_Click(object sender, RoutedEventArgs e)
            => _send.ToggleStickerPopup();

        private void AttachFileButton_Click(object sender, RoutedEventArgs e)
            => _attachments.OnAttachFileButtonClick();

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
            => _send.OnTextBoxKeyDown(sender, e);

        private void TextForMessage_KeyUp(object sender, KeyEventArgs e)
            => _send.OnTextBoxKeyUp(sender, e);

        private void TextForMessage_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                //var textBox = sender as TextBox;
                //textBox.Focus();
            }
            catch { } // игнорируем ошибку если не получилось сфокусироваться на текстбоксе
        }

        private void CloseChatButton(object sender, RoutedEventArgs e)
            => _opening.CloseChat();
    }
}
