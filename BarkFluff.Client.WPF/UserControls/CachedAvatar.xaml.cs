using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Контрол для отображения круглого аватара с кешированием.
    /// Автоматически загружает картинку из кеша или скачивает с сервера.
    /// Показывает плейсхолдер пока картинка загружается.
    /// Применяет динамическую тень на основе среднего цвета аватара.
    /// </summary>
    public partial class CachedAvatar : UserControl
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
                typeof(CachedAvatar),
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
                typeof(CachedAvatar),
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
                typeof(CachedAvatar),
                new PropertyMetadata(null, OnFileIdChanged));

        public string? FileUrl
        {
            get => (string?)GetValue(FileUrlProperty);
            set => SetValue(FileUrlProperty, value);
        }

        /// <summary>
        /// Радиус скругления углов аватара
        /// </summary>
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(CornerRadius),
                typeof(CachedAvatar),
                new PropertyMetadata(new CornerRadius(25), OnCornerRadiusChanged));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        /// <summary>
        /// Включает или выключает динамическую тень на основе цвета аватара
        /// </summary>
        public static readonly DependencyProperty EnableDynamicShadowProperty =
            DependencyProperty.Register(
                nameof(EnableDynamicShadow),
                typeof(bool),
                typeof(CachedAvatar),
                new PropertyMetadata(true));

        public bool EnableDynamicShadow
        {
            get => (bool)GetValue(EnableDynamicShadowProperty);
            set => SetValue(EnableDynamicShadowProperty, value);
        }

        /// <summary>
        /// Радиус размытия тени
        /// </summary>
        public static readonly DependencyProperty ShadowBlurRadiusProperty =
            DependencyProperty.Register(
                nameof(ShadowBlurRadius),
                typeof(double),
                typeof(CachedAvatar),
                new PropertyMetadata(12.0, OnShadowPropertyChanged));

        public double ShadowBlurRadius
        {
            get => (double)GetValue(ShadowBlurRadiusProperty);
            set => SetValue(ShadowBlurRadiusProperty, value);
        }

        /// <summary>
        /// Прозрачность тени
        /// </summary>
        public static readonly DependencyProperty ShadowOpacityProperty =
            DependencyProperty.Register(
                nameof(ShadowOpacity),
                typeof(double),
                typeof(CachedAvatar),
                new PropertyMetadata(0.9, OnShadowPropertyChanged));

        public double ShadowOpacity
        {
            get => (double)GetValue(ShadowOpacityProperty);
            set => SetValue(ShadowOpacityProperty, value);
        }

        /// <summary>
        /// ImageSource для доступа к текущему изображению
        /// </summary>
        public ImageSource? ImageSource => AvatarBrush.ImageSource;

        #endregion

        public CachedAvatar()
        {
            InitializeComponent();
            Loaded += CachedAvatar_Loaded;
            Unloaded += CachedAvatar_Unloaded;
        }

        private void CachedAvatar_Loaded(object sender, RoutedEventArgs e)
        {
            LoadImage();
        }

        private void CachedAvatar_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromFileCached();
        }

        private static void OnFileIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedAvatar cachedAvatar && cachedAvatar.IsLoaded)
            {
                cachedAvatar.LoadImage();
            }
        }

        private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedAvatar cachedAvatar)
            {
                cachedAvatar.AvatarBorder.CornerRadius = (CornerRadius)e.NewValue;
            }
        }

        private static void OnShadowPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedAvatar cachedAvatar)
            {
                cachedAvatar.ShadowEffect.BlurRadius = cachedAvatar.ShadowBlurRadius;
                cachedAvatar.ShadowEffect.Opacity = cachedAvatar.ShadowOpacity;
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
            var imagePath = App.FileCacheService.GetCachedFilePath(_fileId, FileType.Avatar, FileUrl);

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
                bitmapImage.EndInit();

                if (bitmapImage.CanFreeze)
                {
                    bitmapImage.Freeze();
                }

                AvatarBrush.ImageSource = bitmapImage;

                // Обновляем динамическую тень если включено
                if (EnableDynamicShadow && !FileCacheService.IsPlaceholder(imagePath))
                {
                    _ = UpdateDynamicShadow(imagePath);
                }
            }
            catch
            {
                SetPlaceholder();
            }
        }

        /// <summary>
        /// Устанавливает плейсхолдер
        /// </summary>
        private void SetPlaceholder()
        {
            try
            {
                AvatarBrush.ImageSource = new BitmapImage(new Uri(FileCacheService.DefaultPlaceholder, UriKind.RelativeOrAbsolute));
                SetDefaultShadow();
            }
            catch
            {
                // Если не удалось загрузить даже плейсхолдер, оставляем пустым
            }
        }

        /// <summary>
        /// Обновляет динамическую тень на основе среднего цвета изображения
        /// </summary>
        private async Task UpdateDynamicShadow(string imagePath)
        {
            try
            {
                if (App.ColorAnalyzer != null)
                {
                    var averageColor = await App.ColorAnalyzer.GetAverageColorFromUrlAsync(imagePath);
                    Dispatcher.Invoke(() =>
                    {
                        ShadowEffect.Color = averageColor;
                    });
                }
            }
            catch
            {
                Dispatcher.Invoke(() => SetDefaultShadow());
            }
        }

        /// <summary>
        /// Устанавливает тень по умолчанию
        /// </summary>
        private void SetDefaultShadow()
        {
            ShadowEffect.Color = Colors.Gray;
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
            if (fileId != _fileId || fileType != FileType.Avatar)
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
