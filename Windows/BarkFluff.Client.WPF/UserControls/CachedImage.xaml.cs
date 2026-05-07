using BarkFluff.Client.WPF.Services.App.Caching;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        /// <summary>
        /// Срабатывает после успешной загрузки реального изображения (не плейсхолдера).
        /// Передаёт пиксельные размеры, чтобы внешние компоненты могли подстроить контейнер
        /// под пропорции, если они не были известны заранее.
        /// </summary>
        public event Action<int, int>? ImageLoaded;

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
        /// Устанавливает изображение по пути.
        /// Декодирование (включая WebP→PNG) выполняется в фоновом потоке через Task.Run,
        /// чтобы не блокировать UI на больших картинках; готовый <see cref="BitmapImage"/>
        /// замораживается и устанавливается в источник из UI-потока.
        /// </summary>
        private async void SetImage(string imagePath)
        {
            // Placeholder — pack:// URI: декод дешёвый, ставим синхронно.
            if (FileCacheService.IsPlaceholder(imagePath))
            {
                SetPlaceholderFromPath(imagePath);
                return;
            }

            // Считаем целевой DecodePixelWidth до ухода в фон — VisualTreeHelper.GetDpi
            // должен вызываться на UI-потоке.
            var decodeWidth = ResolveDecodePixelWidth();
            var isWebP = imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

            BitmapImage? bitmapImage;
            try
            {
                bitmapImage = await Task.Run(() =>
                {
                    try
                    {
                        return isWebP && File.Exists(imagePath)
                            ? DecodeWebPToBitmap(imagePath, decodeWidth)
                            : DecodeBitmap(imagePath, decodeWidth);
                    }
                    catch
                    {
                        return null;
                    }
                });
            }
            catch
            {
                bitmapImage = null;
            }

            // Контрол мог быть выгружен или _fileId сменился — не подменяем актуальное изображение.
            if (!IsLoaded) return;

            if (bitmapImage == null)
            {
                SetPlaceholder();
                return;
            }

            ContentImage.Source = bitmapImage;

            if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
            {
                ImageLoaded?.Invoke(bitmapImage.PixelWidth, bitmapImage.PixelHeight);
            }
        }

        /// <summary>
        /// Считает целевой <c>DecodePixelWidth</c>: если задан явно — используем его,
        /// иначе берём ActualWidth контрола, скорректированный на DPI монитора.
        /// </summary>
        private int ResolveDecodePixelWidth()
        {
            if (DecodePixelWidth.HasValue && DecodePixelWidth.Value > 0)
            {
                return DecodePixelWidth.Value;
            }

            try
            {
                double w = ActualWidth > 0 ? ActualWidth : Width;
                if (double.IsNaN(w) || w <= 0) return 0;

                var dpi = VisualTreeHelper.GetDpi(this);
                var scale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                var px = (int)Math.Ceiling(w * scale);
                return px > 0 ? px : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static BitmapImage DecodeBitmap(string path, int decodePixelWidth)
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                bitmapImage.DecodePixelWidth = decodePixelWidth;
            }
            bitmapImage.EndInit();
            if (bitmapImage.CanFreeze)
            {
                bitmapImage.Freeze();
            }
            return bitmapImage;
        }

        private static BitmapImage DecodeWebPToBitmap(string webpPath, int decodePixelWidth)
        {
            var webpBytes = File.ReadAllBytes(webpPath);
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(webpBytes);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            ms.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = ms;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                bitmapImage.DecodePixelWidth = decodePixelWidth;
            }
            bitmapImage.EndInit();
            if (bitmapImage.CanFreeze)
            {
                bitmapImage.Freeze();
            }
            return bitmapImage;
        }

        private void SetPlaceholderFromPath(string placeholderPath)
        {
            try
            {
                ContentImage.Source = new BitmapImage(new Uri(placeholderPath, UriKind.RelativeOrAbsolute));
            }
            catch
            {
                // Если даже placeholder не загрузился — оставляем как есть.
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
