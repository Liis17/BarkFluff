using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class ImageMessageContent : UserControl
    {
        private string _fileId = string.Empty;

        public ImageMessageContent()
        {
            InitializeComponent();
            SizeChanged += ImageMessageContent_SizeChanged;
            ImageBorder.MouseLeftButtonDown += ImageBorder_MouseLeftButtonDown;
        }

        public ImageMessageContent(string fileId, string previewUrl, FileType fileType) : this()
        {
            _fileId = fileId;
            CachedContentImage.FileId = fileId;
            CachedContentImage.FileUrl = previewUrl;
            CachedContentImage.FileType = fileType;
        }

        private void ImageMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, ImageBorder.ActualWidth, ImageBorder.ActualHeight);
        }

        private void ImageBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_fileId))
            {
                var msgType = new Services.Erida.MessageType
                {
                    Type = Services.Erida.MessageType.MessageTypeEnum.Info
                };
                App.ErideMessage.AddMessage($"Image clicked: {_fileId}", msgType);
            }
            e.Handled = true;
        }
    }
}
