using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.Classes;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для Profile.xaml
    /// </summary>
    public partial class Profile : UserControl
    {
        private string? _avatarFileId;

        public Profile()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region Dependency Properties

        // Публичное имя
        public static readonly DependencyProperty PublicNameProperty =
            DependencyProperty.Register(nameof(PublicName), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnPublicNameChanged));

        public string PublicName
        {
            get => (string)GetValue(PublicNameProperty);
            set => SetValue(PublicNameProperty, value);
        }

        private static void OnPublicNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.PublicNameTextBlock.Text = e.NewValue?.ToString() ?? string.Empty;
        }

        // Username
        public static readonly DependencyProperty UsernameProperty =
            DependencyProperty.Register(nameof(Username), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnUsernameChanged));

        public string Username
        {
            get => (string)GetValue(UsernameProperty);
            set => SetValue(UsernameProperty, value);
        }

        private static void OnUsernameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
            {
                var username = e.NewValue?.ToString() ?? string.Empty;
                control.UsernameTextBlock.Text = string.IsNullOrEmpty(username) ? string.Empty : $"@{username}";
            }
        }

        // Email
        public static readonly DependencyProperty EmailProperty =
            DependencyProperty.Register(nameof(Email), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnEmailChanged));

        public string Email
        {
            get => (string)GetValue(EmailProperty);
            set => SetValue(EmailProperty, value);
        }

        private static void OnEmailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.EmailTextBlock.Text = e.NewValue?.ToString() ?? string.Empty;
        }

        // User ID
        public static readonly DependencyProperty UserIdProperty =
            DependencyProperty.Register(nameof(UserId), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnUserIdChanged));

        public string UserId
        {
            get => (string)GetValue(UserIdProperty);
            set => SetValue(UserIdProperty, value);
        }

        private static void OnUserIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.UserIdTextBlock.Text = e.NewValue?.ToString() ?? string.Empty;
        }

        // Дата регистрации
        public static readonly DependencyProperty RegistrationDateProperty =
            DependencyProperty.Register(nameof(RegistrationDate), typeof(DateTime?), typeof(Profile),
                new PropertyMetadata(null, OnRegistrationDateChanged));

        public DateTime? RegistrationDate
        {
            get => (DateTime?)GetValue(RegistrationDateProperty);
            set => SetValue(RegistrationDateProperty, value);
        }

        private static void OnRegistrationDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
            {
                var date = (DateTime?)e.NewValue;
                control.RegistrationDateTextBlock.Text = date?.ToString("dd MMM yyyy") ?? string.Empty;
            }
        }

        // Аватар
        public static readonly DependencyProperty AvatarSourceProperty =
            DependencyProperty.Register(nameof(AvatarSource), typeof(ImageSource), typeof(Profile),
                new PropertyMetadata(null, OnAvatarSourceChanged));

        public ImageSource AvatarSource
        {
            get => (ImageSource)GetValue(AvatarSourceProperty);
            set => SetValue(AvatarSourceProperty, value);
        }

        private static void OnAvatarSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
            {
                if (e.NewValue is ImageSource imageSource)
                    control.AvatarBrush.ImageSource = imageSource;
            }
        }

        // Последнее время онлайн
        public static readonly DependencyProperty LastSeenProperty =
            DependencyProperty.Register(nameof(LastSeen), typeof(DateTime?), typeof(Profile),
                new PropertyMetadata(null, OnLastSeenChanged));

        public DateTime? LastSeen
        {
            get => (DateTime?)GetValue(LastSeenProperty);
            set => SetValue(LastSeenProperty, value);
        }

        private static void OnLastSeenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.UpdateLastSeenText();
        }

        // Онлайн статус
        public static readonly DependencyProperty IsOnlineProperty =
            DependencyProperty.Register(nameof(IsOnline), typeof(bool), typeof(Profile),
                new PropertyMetadata(false, OnIsOnlineChanged));

        public bool IsOnline
        {
            get => (bool)GetValue(IsOnlineProperty);
            set => SetValue(IsOnlineProperty, value);
        }

        private static void OnIsOnlineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.UpdateLastSeenText();
        }

        #endregion

        #region Badge Properties

        // Badge 1
        public static readonly DependencyProperty Badge1VisibilityProperty =
            DependencyProperty.Register(nameof(Badge1Visibility), typeof(Visibility), typeof(Profile),
                new PropertyMetadata(Visibility.Collapsed, OnBadge1VisibilityChanged));

        public Visibility Badge1Visibility
        {
            get => (Visibility)GetValue(Badge1VisibilityProperty);
            set => SetValue(Badge1VisibilityProperty, value);
        }

        private static void OnBadge1VisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.Badge1.Visibility = (Visibility)e.NewValue;
        }

        public static readonly DependencyProperty Badge1IconProperty =
            DependencyProperty.Register(nameof(Badge1Icon), typeof(SymbolRegular), typeof(Profile),
                new PropertyMetadata(SymbolRegular.Crown24));

        public SymbolRegular Badge1Icon
        {
            get => (SymbolRegular)GetValue(Badge1IconProperty);
            set => SetValue(Badge1IconProperty, value);
        }

        // Badge 2
        public static readonly DependencyProperty Badge2VisibilityProperty =
            DependencyProperty.Register(nameof(Badge2Visibility), typeof(Visibility), typeof(Profile),
                new PropertyMetadata(Visibility.Collapsed, OnBadge2VisibilityChanged));

        public Visibility Badge2Visibility
        {
            get => (Visibility)GetValue(Badge2VisibilityProperty);
            set => SetValue(Badge2VisibilityProperty, value);
        }

        private static void OnBadge2VisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.Badge2.Visibility = (Visibility)e.NewValue;
        }

        public static readonly DependencyProperty Badge2IconProperty =
            DependencyProperty.Register(nameof(Badge2Icon), typeof(SymbolRegular), typeof(Profile),
                new PropertyMetadata(SymbolRegular.Shield24));

        public SymbolRegular Badge2Icon
        {
            get => (SymbolRegular)GetValue(Badge2IconProperty);
            set => SetValue(Badge2IconProperty, value);
        }

        // Badge 3
        public static readonly DependencyProperty Badge3VisibilityProperty =
            DependencyProperty.Register(nameof(Badge3Visibility), typeof(Visibility), typeof(Profile),
                new PropertyMetadata(Visibility.Collapsed, OnBadge3VisibilityChanged));

        public Visibility Badge3Visibility
        {
            get => (Visibility)GetValue(Badge3VisibilityProperty);
            set => SetValue(Badge3VisibilityProperty, value);
        }

        private static void OnBadge3VisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.Badge3.Visibility = (Visibility)e.NewValue;
        }

        public static readonly DependencyProperty Badge3IconProperty =
            DependencyProperty.Register(nameof(Badge3Icon), typeof(SymbolRegular), typeof(Profile),
                new PropertyMetadata(SymbolRegular.Star24));

        public SymbolRegular Badge3Icon
        {
            get => (SymbolRegular)GetValue(Badge3IconProperty);
            set => SetValue(Badge3IconProperty, value);
        }

        #endregion

        private void UpdateLastSeenText()
        {
            if (IsOnline)
            {
                LastSeenTextBlock.Text = "В сети";
                OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            }
            else if (LastSeen.HasValue)
            {
                var timeAgo = DateTime.Now - LastSeen.Value;

                if (timeAgo.TotalMinutes < 1)
                {
                    LastSeenTextBlock.Text = "Только что";
                    OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                else if (timeAgo.TotalHours < 1)
                {
                    LastSeenTextBlock.Text = $"{(int)timeAgo.TotalMinutes} мин назад";
                    OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                }
                else if (timeAgo.TotalDays < 1)
                {
                    LastSeenTextBlock.Text = $"{(int)timeAgo.TotalHours} ч назад";
                    OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                }
                else
                {
                    LastSeenTextBlock.Text = LastSeen.Value.ToString("dd.MM.yyyy");
                    OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
                }
            }
            else
            {
                LastSeenTextBlock.Text = "Неизвестно";
                OnlineStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
            }
        }

        // Методы для установки данных пользователя
        public void SetUserData(UserProfile userProfile)
        {
            PublicName = userProfile.PublicName;
            Username = userProfile.Username;
            Email = userProfile.Email;
            UserId = userProfile.UserId;
            RegistrationDate = userProfile.RegistrationDate;
            LastSeen = userProfile.LastSeen;
            IsOnline = userProfile.IsOnline;

            if (!string.IsNullOrEmpty(userProfile.AvatarPath))
            {
                try
                {
                    // Пытаемся извлечь fileId из URL
                    _avatarFileId = ExtractFileIdFromUrl(userProfile.AvatarPath);

                    // Используем кеш-сервис для загрузки аватара
                    var imagePath = App.FileCacheService.GetCachedFilePath(_avatarFileId ?? string.Empty, FileType.Avatar, userProfile.AvatarPath);
                    SetAvatarImage(imagePath);

                    // Подписываемся на событие кеширования файла
                    App.FileCacheService.FileCached += OnFileCached;

                    // Отписываемся при выгрузке контрола
                    Unloaded += (s, e) =>
                    {
                        App.FileCacheService.FileCached -= OnFileCached;
                    };
                }
                catch
                {
                    // Использовать аватар по умолчанию
                }
            }

            // Установка баджей
            SetBadges(userProfile.Badges);
        }

        private void OnFileCached(string fileId, string filePath, FileType fileType)
        {
            if (fileId == _avatarFileId && fileType == FileType.Avatar)
            {
                Dispatcher.Invoke(() => SetAvatarImage(filePath));
            }
        }

        private void SetAvatarImage(string imagePath)
        {
            try
            {
                AvatarSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        /// <summary>
        /// Извлекает fileId из URL если возможно
        /// </summary>
        private string? ExtractFileIdFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/');
                if (segments.Length > 0)
                {
                    var lastSegment = segments[^1];
                    var dotIndex = lastSegment.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        lastSegment = lastSegment.Substring(0, dotIndex);
                    }
                    if (Guid.TryParse(lastSegment, out _))
                    {
                        return lastSegment;
                    }
                }
            }
            catch { }
            return null;
        }

        public void SetBadges(BadgeInfo[] badges)
        {
            // Сначала скрываем все баджи
            Badge1Visibility = Visibility.Collapsed;
            Badge2Visibility = Visibility.Collapsed;
            Badge3Visibility = Visibility.Collapsed;

            if (badges == null) return;

            // Показываем до 3 баджей
            for (int i = 0; i < Math.Min(badges.Length, 3); i++)
            {
                switch (i)
                {
                    case 0:
                        Badge1Visibility = Visibility.Visible;
                        Badge1Icon = badges[i].Icon;
                        break;
                    case 1:
                        Badge2Visibility = Visibility.Visible;
                        Badge2Icon = badges[i].Icon;
                        break;
                    case 2:
                        Badge3Visibility = Visibility.Visible;
                        Badge3Icon = badges[i].Icon;
                        break;
                }
            }
        }

        private void OnTabButtonClick(object sender, RoutedEventArgs e)
        {

        }
    }
}
