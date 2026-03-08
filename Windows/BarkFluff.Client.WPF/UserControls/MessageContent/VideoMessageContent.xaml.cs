using BarkFluff.Client.WPF.Pages;
using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class VideoMessageContent : UserControl
    {
        private AttachmentsModel? _attachment;
        private bool _isCached;
        private bool _isDownloading;

        public VideoMessageContent()
        {
            InitializeComponent();
            SizeChanged += VideoMessageContent_SizeChanged;
        }

        public VideoMessageContent(string fileId, string? previewUrl) : this()
        {
            // Обратная совместимость — показываем placeholder
        }

        public VideoMessageContent(AttachmentsModel attachment) : this()
        {
            _attachment = attachment;

            // Загружаем превью через CachedImage если есть PreviewFileId
            if (!string.IsNullOrEmpty(attachment.PreviewFileId))
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewCachedImage.Visibility = Visibility.Visible;
                PreviewCachedImage.FileId = attachment.PreviewFileId;
                PreviewCachedImage.FileUrl = attachment.PreviewUrl;
                PreviewCachedImage.FileType = FileType.Image;
            }
            else if (!string.IsNullOrEmpty(attachment.PreviewUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(attachment.PreviewUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                }
                catch
                {
                    // Placeholder из XAML
                }
            }

            // Проверяем кеш
            _isCached = !string.IsNullOrEmpty(attachment.FileId) &&
                        App.FileCacheService.IsFileCached(attachment.FileId);

            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            if (_isDownloading)
            {
                PlayButton.Visibility = Visibility.Collapsed;
                DownloadButton.Visibility = Visibility.Collapsed;
                ProgressOverlay.Visibility = Visibility.Visible;
            }
            else if (_isCached)
            {
                PlayButton.Visibility = Visibility.Visible;
                DownloadButton.Visibility = Visibility.Collapsed;
                ProgressOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlayButton.Visibility = Visibility.Collapsed;
                DownloadButton.Visibility = Visibility.Visible;
                ProgressOverlay.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (_attachment == null)
            {
                e.Handled = true;
                return;
            }

            // Определяем, кликнули ли на кнопку скачивания
            var hitPoint = e.GetPosition(DownloadButton);
            if (DownloadButton.Visibility == Visibility.Visible &&
                hitPoint.X >= 0 && hitPoint.Y >= 0 &&
                hitPoint.X <= DownloadButton.ActualWidth &&
                hitPoint.Y <= DownloadButton.ActualHeight)
            {
                StartDownload();
                e.Handled = true;
                return;
            }

            if (_isCached)
            {
                OpenVideoPlayer(new List<AttachmentsModel> { _attachment }, 0);
            }

            e.Handled = true;
        }

        private async void StartDownload()
        {
            if (_isDownloading || _attachment == null) return;
            _isDownloading = true;
            UpdateButtonState();

            var progress = new Progress<double>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = $"{(int)(p * 100)}%";
                });
            });

            var result = await App.FileCacheService.DownloadAndCacheFileWithProgressAsync(
                _attachment.FileId, FileType.Video, null, progress);

            _isDownloading = false;
            _isCached = result != null;
            UpdateButtonState();
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
