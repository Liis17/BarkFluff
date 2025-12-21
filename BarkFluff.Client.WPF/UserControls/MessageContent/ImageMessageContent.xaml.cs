using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class ImageMessageContent : UserControl
    {
        public ImageMessageContent()
        {
            InitializeComponent();
            SizeChanged += ImageMessageContent_SizeChanged;
        }

        public ImageMessageContent(string fileId, string previewUrl, FileType fileType) : this()
        {
            CachedContentImage.FileId = fileId;
            CachedContentImage.FileUrl = previewUrl;
            CachedContentImage.FileType = fileType;
        }

        private void ImageMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, ImageBorder.ActualWidth, ImageBorder.ActualHeight);
        }

        public void SetImageSource(ImageSource source)
        {
            // For backward compatibility - manually set image if needed
        }
    }
}
