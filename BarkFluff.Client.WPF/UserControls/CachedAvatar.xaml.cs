using BarkFluff.Client.WPF.Services.App.Caching;

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
            // Обновляем видимость элементов в зависимости от типа аватара
            UpdateAvatarVisibility();

            // Для типов SavedChat и UserWithoutAvatar не загружаем изображение
            if (AvatarType == UserControls.AvatarType.SavedChat ||
                AvatarType == UserControls.AvatarType.UserWithoutAvatar)
            {
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
