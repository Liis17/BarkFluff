using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class ImageMessageContent : UserControl
    {
        private string _fileId = string.Empty;
        private AttachmentsModel _attachment;

        public ImageMessageContent()
        {
            InitializeComponent();
            SizeChanged += ImageMessageContent_SizeChanged;
            ImageBorder.MouseLeftButtonDown += ImageBorder_MouseLeftButtonDown;
        }

        public ImageMessageContent(AttachmentsModel attachment) : this()
        {
            _attachment = attachment;
            _fileId = attachment.FileId;

            // Используем PreviewFileId для превью в сообщении (как раньше)
            var previewId = !string.IsNullOrEmpty(attachment.PreviewFileId)
                ? attachment.PreviewFileId
                : attachment.FileId;

            CachedContentImage.FileId = previewId;
            CachedContentImage.FileUrl = attachment.PreviewUrl;
            CachedContentImage.FileType = attachment.Type == BarkFluff.Proto.Shared.MessageAttachmentType.Gif
                ? FileType.Gif
                : FileType.Image;
        }

        private void ImageMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, ImageBorder.ActualWidth, ImageBorder.ActualHeight);
        }

        private void ImageBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_attachment != null)
            {
                // Открыть ImageViewer с одним изображением
                OpenImageViewer(new List<AttachmentsModel> { _attachment }, 0);
            }
            e.Handled = true;
        }

        private void OpenImageViewer(List<AttachmentsModel> attachments, int currentIndex)
        {
            var messengerPage = FindParent<MessengerPage>(this);
            messengerPage?.OpenImageViewer(attachments, currentIndex);
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T ? (T)parent : FindParent<T>(parent);
        }
    }
}
