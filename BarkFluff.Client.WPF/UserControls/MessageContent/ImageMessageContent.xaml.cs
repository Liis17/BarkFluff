using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class ImageMessageContent : UserControl
    {
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_MAX_HEIGHT = 300;

        private string? _fileId;
        private bool _isSubscribedToFileCached;

        public ImageMessageContent()
        {
            InitializeComponent();
            SizeChanged += ImageMessageContent_SizeChanged;
            Unloaded += ImageMessageContent_Unloaded;
        }

        public ImageMessageContent(string fileId, string previewUrl, FileType fileType) : this()
        {
            _fileId = fileId;
            var imagePath = App.FileCacheService.GetCachedFilePath(fileId, fileType, previewUrl);
            LoadImage(imagePath);
            SubscribeToFileCached();
        }

        private void ImageMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, ImageBorder.ActualWidth, ImageBorder.ActualHeight);
        }

        private void ImageMessageContent_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromFileCached();
            SizeChanged -= ImageMessageContent_SizeChanged;
            Unloaded -= ImageMessageContent_Unloaded;
        }

        public void LoadImage(string imagePath)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.DecodePixelWidth = IMAGE_MAX_WIDTH;
                bitmapImage.EndInit();
                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }
                ContentImage.Source = bitmapImage;
            }
            catch
            {
                ContentImage.Source = new BitmapImage(new Uri(FileCacheService.DefaultPlaceholder, UriKind.RelativeOrAbsolute));
            }
        }

        public void SetImageSource(ImageSource source)
        {
            ContentImage.Source = source;
        }

        private void SubscribeToFileCached()
        {
            if (!_isSubscribedToFileCached)
            {
                App.FileCacheService.FileCached += OnFileCached;
                _isSubscribedToFileCached = true;
            }
        }

        private void UnsubscribeFromFileCached()
        {
            if (_isSubscribedToFileCached)
            {
                App.FileCacheService.FileCached -= OnFileCached;
                _isSubscribedToFileCached = false;
            }
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileId != _fileId)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                LoadImage(filePath);
            });

            UnsubscribeFromFileCached();
        }
    }
}
