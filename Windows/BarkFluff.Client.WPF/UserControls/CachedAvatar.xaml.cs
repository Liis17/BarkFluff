using BarkFluff.Client.WPF.Services.App.Caching;

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        /// Тип аватара (изображение, избранный чат, пользователь без аватара)
        /// </summary>
        public static readonly DependencyProperty AvatarTypeProperty =
            DependencyProperty.Register(
                nameof(AvatarType),
                typeof(UserControls.AvatarType),
                typeof(CachedAvatar),
                new PropertyMetadata(UserControls.AvatarType.Image, OnAvatarTypeChanged));

        public UserControls.AvatarType AvatarType
        {
            get => (UserControls.AvatarType)GetValue(AvatarTypeProperty);
            set => SetValue(AvatarTypeProperty, value);
        }

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

        public static readonly DependencyProperty AvatarSizeProperty =
            DependencyProperty.Register(
                nameof(AvatarSize),
                typeof(AvatarSize),
                typeof(CachedAvatar),
                new PropertyMetadata(AvatarSize.Normal, OnAvatarSizeChanged));

        private static void OnAvatarSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedAvatar cachedAvatar && cachedAvatar.IsLoaded)
            {
                cachedAvatar.LoadImage();
            }
        }

        public UserControls.AvatarSize AvatarSize
        {
            get => (UserControls.AvatarSize)GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
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
            UpdateAvatarVisibility();
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

        private static void OnAvatarTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CachedAvatar cachedAvatar && cachedAvatar.IsLoaded)
            {
                cachedAvatar.UpdateAvatarVisibility();
                cachedAvatar.LoadImage();
            }
        }

        /// <summary>
        /// Загружает изображение из кеша или начинает загрузку
        /// </summary>
        private void LoadImage()
        {
            Debug.WriteLine($"[CachedAvatar] LoadImage called. AvatarType={AvatarType}, FileId={FileId}, FileUrl={FileUrl}, PreviewFileId={PreviewFileId}, Name={Name}");

            // Обновляем видимость элементов в зависимости от типа аватара
            UpdateAvatarVisibility();

            // Для типов SavedChat и UserWithoutAvatar не загружаем изображение
            if (AvatarType == UserControls.AvatarType.SavedChat ||
                AvatarType == UserControls.AvatarType.UserWithoutAvatar)
            {
                Debug.WriteLine($"[CachedAvatar] Skipping load — AvatarType={AvatarType}");
                UnsubscribeFromFileCached();
                return;
            }

            // Отписываемся от предыдущих событий
            UnsubscribeFromFileCached();

            // Определяем какой fileId использовать (предпочитаем preview если есть)
            var effectiveFileId = !string.IsNullOrEmpty(PreviewFileId) ? PreviewFileId : FileId;

            // Если fileId пустой, пытаемся извлечь из URL
            if (string.IsNullOrEmpty(effectiveFileId) && !string.IsNullOrEmpty(FileUrl))
            {
                effectiveFileId = FileCacheService.ExtractFileIdFromUrl(FileUrl);
                Debug.WriteLine($"[CachedAvatar] Extracted fileId from URL: {effectiveFileId}");
            }

            _fileId = effectiveFileId;

            // Если fileId всё ещё пустой, показываем иконку пользователя без аватара
            if (string.IsNullOrEmpty(_fileId))
            {
                Debug.WriteLine($"[CachedAvatar] No fileId available — showing placeholder icon");
                SetPlaceholder();
                return;
            }

            Debug.WriteLine($"[CachedAvatar] Loading image for fileId={_fileId}, FileUrl={FileUrl}");

            // Подписываемся ПЕРЕД запросом кеша, чтобы не пропустить событие
            SubscribeToFileCached();

            // Получаем путь к файлу из кеша (может запустить фоновую загрузку)
            var imagePath = App.FileCacheService.GetCachedFilePath(_fileId, FileType.Avatar, FileUrl);

            Debug.WriteLine($"[CachedAvatar] GetCachedFilePath returned: {imagePath}");

            if (!FileCacheService.IsPlaceholder(imagePath))
            {
                // Файл уже в кеше — показываем реальное изображение
                Debug.WriteLine($"[CachedAvatar] File found in cache — showing image from: {imagePath}");
                UnsubscribeFromFileCached();
                ShowImageFromPath(imagePath);
            }
            else
            {
                Debug.WriteLine($"[CachedAvatar] File NOT in cache — showing icon, waiting for download...");
                // Показываем иконку пока файл загружается
                ShowUserWithoutAvatarIcon();

                // Повторная проверка: загрузка могла завершиться между GetCachedFilePath и подпиской
                if (App.FileCacheService.IsFileCached(_fileId))
                {
                    var cachedPath = App.FileCacheService.GetCachedFilePath(_fileId, FileType.Avatar, FileUrl);
                    if (!FileCacheService.IsPlaceholder(cachedPath))
                    {
                        Debug.WriteLine($"[CachedAvatar] File appeared in cache after re-check: {cachedPath}");
                        UnsubscribeFromFileCached();
                        ShowImageFromPath(cachedPath);
                    }
                }
            }
        }

        /// <summary>
        /// Обновляет видимость элементов аватара в зависимости от типа
        /// </summary>
        private void UpdateAvatarVisibility()
        {
            bool showImage = AvatarType == UserControls.AvatarType.Image;
            bool showSavedChat = AvatarType == UserControls.AvatarType.SavedChat;
            bool showUserWithoutAvatar = AvatarType == UserControls.AvatarType.UserWithoutAvatar;

            AvatarBorder.Visibility = showImage ? Visibility.Visible : Visibility.Collapsed;
            SavedChatAvatar.Visibility = showSavedChat ? Visibility.Visible : Visibility.Collapsed;
            UserWithoutAvatar.Visibility = showUserWithoutAvatar ? Visibility.Visible : Visibility.Collapsed;

            switch (AvatarSize)
            {
                case AvatarSize.Normal:
                    UserWithoutAvatar.Width = UserWithoutAvatar.Height = SavedChatAvatar.Width = SavedChatAvatar.Height = MainAvatar.Width = MainAvatar.Height = AvatarBorder.Width = AvatarBorder.Height = 50;
                    UserWithoutAvatar.CornerRadius = SavedChatAvatar.CornerRadius = AvatarBorder.CornerRadius = new CornerRadius(25);
                    SavedChatIcon.Width = SavedChatIcon.Height = UserIcon.FontSize = UserIcon.Width = UserIcon.Height = UserIcon.FontSize = 30;
                    break;
                case AvatarSize.Little:
                    UserWithoutAvatar.Width = UserWithoutAvatar.Height = SavedChatAvatar.Width = SavedChatAvatar.Height = MainAvatar.Width = MainAvatar.Height = AvatarBorder.Width = AvatarBorder.Height = 35;
                    UserWithoutAvatar.CornerRadius = SavedChatAvatar.CornerRadius = AvatarBorder.CornerRadius = new CornerRadius(17);
                    SavedChatIcon.Width = SavedChatIcon.Height = UserIcon.FontSize = UserIcon.Width = UserIcon.Height = UserIcon.FontSize = 24;
                    break;
                case AvatarSize.VeryLittle:
                    UserWithoutAvatar.Width = UserWithoutAvatar.Height = SavedChatAvatar.Width = SavedChatAvatar.Height = MainAvatar.Width = MainAvatar.Height = AvatarBorder.Width = AvatarBorder.Height = 23;
                    UserWithoutAvatar.CornerRadius = SavedChatAvatar.CornerRadius = AvatarBorder.CornerRadius = new CornerRadius(11);
                    SavedChatIcon.Width = SavedChatIcon.Height = UserIcon.FontSize = UserIcon.Width = UserIcon.Height = UserIcon.FontSize = 16;
                    break;
                case AvatarSize.Big:
                    UserWithoutAvatar.Width = UserWithoutAvatar.Height = SavedChatAvatar.Width = SavedChatAvatar.Height = MainAvatar.Width = MainAvatar.Height = AvatarBorder.Width = AvatarBorder.Height = 110;
                    UserWithoutAvatar.CornerRadius = SavedChatAvatar.CornerRadius = AvatarBorder.CornerRadius = new CornerRadius(55);
                    SavedChatIcon.Width = SavedChatIcon.Height = UserIcon.FontSize = UserIcon.Width = UserIcon.Height = UserIcon.FontSize = 80;
                    break;
            }
        }

        /// <summary>
        /// Показывает реальное изображение из файла и переключает видимость на AvatarBorder.
        /// Декод BitmapImage выполняется в фоновом потоке с DecodePixelWidth под реальный
        /// размер аватара (50/35/23/110 px × DPI), чтобы не декодировать большое
        /// исходное изображение на UI-потоке.
        /// </summary>
        private async void ShowImageFromPath(string imagePath)
        {
            // DPI и целевая ширина — вычисляем на UI-потоке до ухода в фон.
            int decodeWidth = ResolveAvatarDecodePixelWidth();

            BitmapImage? bitmapImage;
            try
            {
                bitmapImage = await Task.Run(() =>
                {
                    try
                    {
                        var bm = new BitmapImage();
                        bm.BeginInit();
                        bm.UriSource = new Uri(imagePath, UriKind.RelativeOrAbsolute);
                        bm.CacheOption = BitmapCacheOption.OnLoad;
                        if (decodeWidth > 0)
                        {
                            bm.DecodePixelWidth = decodeWidth;
                        }
                        bm.EndInit();
                        if (bm.CanFreeze) bm.Freeze();
                        return bm;
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

            if (!IsLoaded || bitmapImage == null)
            {
                if (bitmapImage == null) SetPlaceholder();
                return;
            }

            AvatarBrush.ImageSource = bitmapImage;

            // Показываем Border с изображением, скрываем иконки.
            AvatarBorder.Visibility = Visibility.Visible;
            UserWithoutAvatar.Visibility = Visibility.Collapsed;
            SavedChatAvatar.Visibility = Visibility.Collapsed;

            // Обновляем динамическую тень если включено.
            if (EnableDynamicShadow)
            {
                _ = UpdateDynamicShadow(imagePath);
            }
        }

        private int ResolveAvatarDecodePixelWidth()
        {
            try
            {
                double w = MainAvatar.Width;
                if (double.IsNaN(w) || w <= 0)
                {
                    w = AvatarBorder.Width;
                }
                if (double.IsNaN(w) || w <= 0)
                {
                    return 0;
                }

                var dpi = VisualTreeHelper.GetDpi(this);
                var scale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                return (int)Math.Ceiling(w * scale);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Показывает иконку пользователя без аватара (без изменения AvatarType)
        /// </summary>
        private void ShowUserWithoutAvatarIcon()
        {
            AvatarBorder.Visibility = Visibility.Collapsed;
            SavedChatAvatar.Visibility = Visibility.Collapsed;
            UserWithoutAvatar.Visibility = Visibility.Visible;
            SetDefaultShadow();
        }

        /// <summary>
        /// Устанавливает плейсхолдер — показывает иконку без аватара (без изменения AvatarType)
        /// </summary>
        private void SetPlaceholder()
        {
            ShowUserWithoutAvatarIcon();
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
            Debug.WriteLine($"[CachedAvatar] OnFileCached: fileId={fileId}, filePath={filePath}, fileType={fileType}, expected _fileId={_fileId}");

            if (fileId != _fileId || fileType != FileType.Avatar)
            {
                return;
            }

            Debug.WriteLine($"[CachedAvatar] File matched! Showing image from: {filePath}");
            Dispatcher.Invoke(() =>
            {
                ShowImageFromPath(filePath);
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
