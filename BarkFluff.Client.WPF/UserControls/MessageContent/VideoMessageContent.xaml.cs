using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class VideoMessageContent : UserControl
    {
        private const int IMAGE_MAX_WIDTH = 400;

        private string? _fileId;
        private bool _isSubscribedToFileCached;

        public VideoMessageContent()
        {
            InitializeComponent();
            SizeChanged += VideoMessageContent_SizeChanged;
            Unloaded += VideoMessageContent_Unloaded;
        }

        public VideoMessageContent(string fileId, string? previewUrl) : this()
        {
            _fileId = fileId;

            if (!string.IsNullOrEmpty(previewUrl))
            {
                var imagePath = App.FileCacheService.GetCachedFilePath(fileId, FileType.Video, previewUrl);
                LoadPreviewImage(imagePath);
                SubscribeToFileCached();
            }
            else
            {
                PreviewImage.Source = new BitmapImage(new Uri(FileCacheService.DefaultPlaceholder, UriKind.RelativeOrAbsolute));
            }
        }

        private void VideoMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, VideoBorder.ActualWidth, VideoBorder.ActualHeight);
        }

        private void VideoMessageContent_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromFileCached();
            SizeChanged -= VideoMessageContent_SizeChanged;
            Unloaded -= VideoMessageContent_Unloaded;
        }

        public void LoadPreviewImage(string imagePath)
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
                PreviewImage.Source = bitmapImage;
            }
            catch
            {
                PreviewImage.Source = new BitmapImage(new Uri(FileCacheService.DefaultPlaceholder, UriKind.RelativeOrAbsolute));
            }
        }

        public void SetPreviewSource(ImageSource source)
        {
            PreviewImage.Source = source;
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
                LoadPreviewImage(filePath);
            });

            UnsubscribeFromFileCached();
        }
    }
}
