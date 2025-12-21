using System.Windows;
using System.Windows.Controls;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class VideoMessageContent : UserControl
    {
        public VideoMessageContent()
        {
            InitializeComponent();
            SizeChanged += VideoMessageContent_SizeChanged;
        }

        public VideoMessageContent(string fileId, string? previewUrl) : this()
        {
            CachedPreviewImage.FileId = fileId;
            CachedPreviewImage.FileUrl = previewUrl;
            CachedPreviewImage.FileType = FileType.Video;
        }

        private void VideoMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, VideoBorder.ActualWidth, VideoBorder.ActualHeight);
        }
    }
}
