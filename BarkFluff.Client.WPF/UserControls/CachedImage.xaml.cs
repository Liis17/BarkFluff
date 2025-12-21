using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Контрол для отображения картинок с кешированием.
    /// Автоматически загружает картинку из кеша или скачивает с сервера.
    /// Показывает плейсхолдер пока картинка загружается.
    /// </summary>
    public partial class CachedImage : UserControl
    {
        private string? _fileId;
        private bool _isSubscribedToFileCached;

        #region Dependency Properties

        /// <summary>
        /// Идентификатор файла для загрузки
        /// </summary>
        public static readonly DependencyProperty FileIdProperty =
            DependencyProperty.Register(
                nameof(FileId),
                typeof(string),
                typeof(CachedImage),
                new PropertyMetadata(null, OnFileIdChanged));

        public string? FileId
        {
            get => (string?)GetValue(FileIdProperty);
            set => SetValue(FileIdProperty, value);
        }

        /// <summary>
        /// Идентификатор превью файла (если есть)
        /// </summary>
        public static readonly DependencyProperty PreviewFileIdProperty =
            DependencyProperty.Register(
                nameof(PreviewFileId),
                typeof(string),
                typeof(CachedImage),
                new PropertyMetadata(null, OnFileIdChanged));

        public string? PreviewFileId
        {
            get => (string?)GetValue(PreviewFileIdProperty);
            set => SetValue(PreviewFileIdProperty, value);
        }

        /// <summary>
        /// URL для загрузки файла (опционально, если нужен прямой URL)
        /// </summary>
        public static readonly DependencyProperty FileUrlProperty =
            DependencyProperty.Register(
                nameof(FileUrl),
                typeof(string),
                typeof(CachedImage),
                new PropertyMetadata(null, OnFileIdChanged));

        public string? FileUrl
        {
            get => (string?)GetValue(FileUrlProperty);
            set => SetValue(FileUrlProperty, value);
        }

        /// <summary>
        /// Тип файла для кеширования
        /// </summary>
        public static readonly DependencyProperty FileTypeProperty =
            DependencyProperty.Register(
                nameof(FileType),
                typeof(FileType),
                typeof(CachedImage),
                new PropertyMetadata(FileType.Image, OnFileIdChanged));

        public FileType FileType
        {
            get => (FileType)GetValue(FileTypeProperty);
            set => SetValue(FileTypeProperty, value);
        }

        /// <summary>
        /// Режим растяжения изображения
        /// </summary>
        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.Register(
                nameof(Stretch),
                typeof(Stretch),
                typeof(CachedImage),
                new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>
        /// Максимальная ширина для декодирования изображения (для оптимизации памяти)
        /// </summary>
        public static readonly DependencyProperty DecodePixelWidthProperty =
            DependencyProperty.Register(
                nameof(DecodePixelWidth),
                typeof(int?),
                typeof(CachedImage),
                new PropertyMetadata(null));

        public int? DecodePixelWidth
        {
            get => (int?)GetValue(DecodePixelWidthProperty);
            set => SetValue(DecodePixelWidthProperty, value);
        }

        /// <summary>
        /// ImageSource для доступа к текущему изображению
        /// </summary>
        public ImageSource? ImageSource => ContentImage.Source;

        #endregion

        public CachedImage()
        {
            InitializeComponent();
            Loaded += CachedImage_Loaded;
            Unloaded += CachedImage_Unloaded;
        }

        private void CachedImage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadImage();
        }

        private void CachedImage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromFileCached();
        }

        private static void OnFileIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedImage cachedImage && cachedImage.IsLoaded)
            {
                cachedImage.LoadImage();
            }
        }

        private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedImage cachedImage)
            {
                cachedImage.ContentImage.Stretch = (Stretch)e.NewValue;
            }
        }

        /// <summary>
        /// Загружает изображение из кеша или начинает загрузку
        /// </summary>
        private void LoadImage()
        {
            // Отписываемся от предыдущих событий
            UnsubscribeFromFileCached();

            // Определяем какой fileId использовать (предпочитаем preview если есть)
            var effectiveFileId = !string.IsNullOrEmpty(PreviewFileId) ? PreviewFileId : FileId;

            // Если fileId пустой, пытаемся извлечь из URL
            if (string.IsNullOrEmpty(effectiveFileId) && !string.IsNullOrEmpty(FileUrl))
            {
                effectiveFileId = FileCacheService.ExtractFileIdFromUrl(FileUrl);
            }

            _fileId = effectiveFileId;

            // Если fileId всё ещё пустой, показываем плейсхолдер
            if (string.IsNullOrEmpty(_fileId))
            {
                SetPlaceholder();
                return;
            }

            // Получаем путь к файлу из кеша
            var imagePath = App.FileCacheService.GetCachedFilePath(_fileId, FileType, FileUrl);

            // Загружаем изображение
            SetImage(imagePath);

            // Если это плейсхолдер, подписываемся на событие кеширования
            if (FileCacheService.IsPlaceholder(imagePath))
            {
                SubscribeToFileCached();
            }
        }

        /// <summary>
        /// Устанавливает изображение по пути
        /// </summary>
        private void SetImage(string imagePath)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;

                if (DecodePixelWidth.HasValue && DecodePixelWidth.Value > 0)
                {
                    bitmapImage.DecodePixelWidth = DecodePixelWidth.Value;
                }

                bitmapImage.EndInit();

                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }

                ContentImage.Source = bitmapImage;
            }
            catch
            {
                SetPlaceholder();
            }
        }

        /// <summary>
        /// Устанавливает плейсхолдер в зависимости от типа файла
        /// </summary>
        private void SetPlaceholder()
        {
            try
            {
                var placeholderPath = FileCacheService.GetPlaceholderForType(FileType);
                ContentImage.Source = new BitmapImage(new Uri(placeholderPath, UriKind.RelativeOrAbsolute));
            }
            catch
            {
                // Если не удалось загрузить даже плейсхолдер, оставляем пустым
            }
        }

        private void SubscribeToFileCached()
        {
            if (!_isSubscribedToFileCached && App.FileCacheService != null)
            {
                App.FileCacheService.FileCached += OnFileCached;
                _isSubscribedToFileCached = true;
            }
        }

        private void UnsubscribeFromFileCached()
        {
            if (_isSubscribedToFileCached && App.FileCacheService != null)
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
                SetImage(filePath);
                UnsubscribeFromFileCached();
            });
        }

        /// <summary>
        /// Принудительно обновляет изображение
        /// </summary>
        public void Refresh()
        {
            LoadImage();
        }
    }
}
