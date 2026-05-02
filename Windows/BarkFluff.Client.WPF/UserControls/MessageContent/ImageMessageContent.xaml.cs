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
        private bool _dimensionsLocked;

        /// <summary>Вычисленная ширина изображения после масштабирования под ограничения</summary>
        public double ComputedWidth { get; private set; } = 300;
        /// <summary>Вычисленная высота изображения после масштабирования под ограничения</summary>
        public double ComputedHeight { get; private set; } = 200;

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

            bool hasInitialDimensions = attachment.ImageWidth > 0 && attachment.ImageHeight > 0;

            // Применяем размер placeholder'а на основе реальных размеров изображения
            ApplyImageDimensions(attachment.ImageWidth, attachment.ImageHeight);

            if (hasInitialDimensions)
            {
                // Размеры уже известны — фиксируем сразу, чтобы загрузка превью/полного
                // изображения не пересчитывала контейнер.
                _dimensionsLocked = true;
            }
            else
            {
                // Размеры неизвестны — подписываемся на одноразовый пересчёт
                // по факту загрузки реального изображения.
                CachedContentImage.ImageLoaded += OnImageLoaded;
            }

            // Используем PreviewFileId для превью в сообщении
            var previewId = !string.IsNullOrEmpty(attachment.PreviewFileId)
                ? attachment.PreviewFileId
                : attachment.FileId;

            CachedContentImage.FileId = previewId;
            CachedContentImage.FileUrl = attachment.PreviewUrl;
            CachedContentImage.FileType = attachment.Type == BarkFluff.Proto.Shared.MessageAttachmentType.Gif
                ? FileType.Gif
                : FileType.Image;
        }

        private void OnImageLoaded(int pixelWidth, int pixelHeight)
        {
            if (_dimensionsLocked) return;
            _dimensionsLocked = true;

            Dispatcher.Invoke(() =>
            {
                ApplyImageDimensions(pixelWidth, pixelHeight);
                CachedContentImage.ImageLoaded -= OnImageLoaded;

                // Поднимаем MinWidth родительского MessageBubble — иначе MediaContentPresenter
                // и Border остаются ограниченными старым (fallback) размером и обрезают картинку.
                var bubble = FindParent<MessageBubble>(this);
                if (bubble != null && ComputedWidth > bubble.MinWidth)
                {
                    bubble.MinWidth = ComputedWidth;
                }

                InvalidateMeasure();
                UpdateLayout();
            });
        }

        /// <summary>
        /// Вычисляет итоговые размеры контейнера так, чтобы он точно вписывался
        /// в максимум 400×600 с сохранением соотношения сторон картинки.
        /// Картинка внутри (Stretch=Uniform) ляжет ровно по краям без полей и без обрезки,
        /// поскольку контейнер сам имеет те же пропорции, что и картинка.
        /// </summary>
        private void ApplyImageDimensions(int srcWidth, int srcHeight)
        {
            const double maxWidth = 400;
            const double maxHeight = 600;
            // Нейтральная квадратная заглушка, если реальные размеры ещё не известны.
            const double fallbackWidth = 220;
            const double fallbackHeight = 220;

            double w, h;
            if (srcWidth > 0 && srcHeight > 0)
            {
                // Любая картинка (и большая, и маленькая) растягивается/сжимается так,
                // чтобы максимально заполнить рамку 400×600 без обрезки.
                double scale = Math.Min(maxWidth / srcWidth, maxHeight / srcHeight);
                w = Math.Round(srcWidth * scale);
                h = Math.Round(srcHeight * scale);
            }
            else
            {
                w = fallbackWidth;
                h = fallbackHeight;
            }

            ComputedWidth = w;
            ComputedHeight = h;
            this.Width = w;
            this.Height = h;
            // Не позволяем облачку растягивать контрол шире его реальной ширины.
            this.HorizontalAlignment = HorizontalAlignment.Left;
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
