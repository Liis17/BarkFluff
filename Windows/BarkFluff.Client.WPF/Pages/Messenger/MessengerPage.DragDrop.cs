using System.Windows;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// XAML event handlers перетаскивания файлов в область открытого чата.
    /// Тонкие делегирующие обёртки поверх <see cref="Messenger.Controllers.AttachmentController"/>.
    /// </summary>
    public partial class MessengerPage
    {
        private void OpenedChat_DragEnter(object sender, DragEventArgs e)
            => _attachments.OnDragEnter(e);

        private void OpenedChat_DragOver(object sender, DragEventArgs e)
            => _attachments.OnDragOver(e);

        private void OpenedChat_DragLeave(object sender, DragEventArgs e)
            => _attachments.OnDragLeave(e);

        private void OpenedChat_Drop(object sender, DragEventArgs e)
            => _attachments.OnDrop(e);
    }
}
