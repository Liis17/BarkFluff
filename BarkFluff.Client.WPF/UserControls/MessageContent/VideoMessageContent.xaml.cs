using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class VideoMessageContent : UserControl
    {
        private AttachmentsModel? _attachment;

        public VideoMessageContent()
        {
            InitializeComponent();
            SizeChanged += VideoMessageContent_SizeChanged;
        }

        public VideoMessageContent(string fileId, string? previewUrl) : this()
        {
            // Оставлено для обратной совместимости
            CachedPreviewImage.FileId = fileId;
            CachedPreviewImage.FileUrl = previewUrl;
            CachedPreviewImage.FileType = FileType.Video;
        }

        public VideoMessageContent(AttachmentsModel attachment) : this()
        {
            _attachment = attachment;

            // Используем PreviewFileId для превью ИЗОБРАЖЕНИЯ
            var previewId = !string.IsNullOrEmpty(attachment.PreviewFileId)
                ? attachment.PreviewFileId
                : attachment.FileId;

            CachedPreviewImage.FileId = previewId;
            CachedPreviewImage.FileUrl = attachment.PreviewUrl;
            CachedPreviewImage.FileType = FileType.Image; // ВАЖНО: Image, не Video!
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_attachment != null)
            {
                OpenVideoPlayer(new List<AttachmentsModel> { _attachment }, 0);
            }
            e.Handled = true;
        }

        private void OpenVideoPlayer(List<AttachmentsModel> attachments, int currentIndex)
        {
            var messengerPage = FindParent<MessengerPage>(this);
            messengerPage?.OpenVideoPlayer(attachments, currentIndex);
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T ? (T)parent : FindParent<T>(parent);
        }

        private void VideoMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, VideoBorder.ActualWidth, VideoBorder.ActualHeight);
        }
    }
}
