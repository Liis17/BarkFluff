using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.Classes;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для Profile.xaml
    /// </summary>
    public partial class Profile : UserControl
    {
        private bool _isCurrentUser = false;

        public Profile()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

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
                control.UsernameInfoTextBlock.Text = string.IsNullOrEmpty(username) ? string.Empty : $"@{username}";
            }
        }

        // FirstName
        public static readonly DependencyProperty FirstNameProperty =
            DependencyProperty.Register(nameof(FirstName), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnNameChanged));

        public string FirstName
        {
            get => (string)GetValue(FirstNameProperty);
            set => SetValue(FirstNameProperty, value);
        }

        // LastName
        public static readonly DependencyProperty LastNameProperty =
            DependencyProperty.Register(nameof(LastName), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnNameChanged));

        public string LastName
        {
            get => (string)GetValue(LastNameProperty);
            set => SetValue(LastNameProperty, value);
        }

        private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
                control.UpdatePublicName();
        }

        private void UpdatePublicName()
        {
            var fullName = $"{FirstName} {LastName}".Trim();
            PublicNameTextBlock.Text = string.IsNullOrEmpty(fullName) ? Username : fullName;
        }

        // Description
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(Profile),
                new PropertyMetadata(string.Empty, OnDescriptionChanged));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Profile control)
            {
                var description = e.NewValue?.ToString() ?? string.Empty;
                control.DescriptionTextBlock.Text = description;
                control.DescriptionSection.Visibility = string.IsNullOrWhiteSpace(description)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
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
            {
                var email = e.NewValue?.ToString() ?? string.Empty;
                control.EmailTextBlock.Text = email;
                // Email показывается только для своего профиля и если он не пустой
                control.EmailSection.Visibility = (control._isCurrentUser && !string.IsNullOrWhiteSpace(email))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
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
                if (date.HasValue)
                {
                    control.RegistrationDateTextBlock.Text = date.Value.ToString("dd MMMM yyyy");
                }
                else
                {
                    control.RegistrationDateTextBlock.Text = "Неизвестно";
                }
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
            // Online status UI has been removed, keeping property for compatibility
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
            // Online status UI has been removed, keeping property for compatibility
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

        #region Profile Loading Methods

        /// <summary>
        /// Загружает профиль текущего пользователя из App.GParam
        /// </summary>
        public void LoadCurrentUserProfile()
        {
            _isCurrentUser = true;

            UserId = App.GParam.UserId.ToString();
            Username = App.GParam.UserName;
            FirstName = App.GParam.FirstName;
            LastName = App.GParam.LastName;
            Description = App.GParam.Description;
            Email = App.GParam.Email;

            RegistrationDate = App.GParam.RegistrationDate;

            UpdatePublicName();

            // Показываем email для своего профиля
            EmailSection.Visibility = Visibility.Visible;

            // Загружаем аватар через кеш
            LoadAvatar(App.GParam.PictureUrl);

            // Скрываем баджи по умолчанию
            SetBadges(null);
        }

        /// <summary>
        /// Загружает профиль другого пользователя по userId
        /// </summary>
        /// <param name="userId">ID пользователя для загрузки</param>
        public async void LoadUserProfile(long userId)
        {
            _isCurrentUser = userId == App.GParam.UserId;

            if (_isCurrentUser)
            {
                LoadCurrentUserProfile();
                return;
            }

            try
            {
                var response = await App.ServerCommunication.GetUserData(App.GParam, userId);

                if (response.Data == null)
                {
                    App.ErideMessage?.AddMessage($"Не удалось загрузить профиль пользователя {userId}: данные не получены",
                        new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    return;
                }

                UserId = response.Data.Id.ToString();
                Username = response.Data.Username;
                FirstName = response.Data.FirstName;
                LastName = response.Data.LastName;
                Description = response.Data.Description;

                // Конвертируем Timestamp в DateTime
                RegistrationDate = response.Data.RegistrationDate;

                UpdatePublicName();

                // Скрываем email для чужого профиля
                EmailSection.Visibility = Visibility.Collapsed;

                // Загружаем аватар
                var avatarUrl = !string.IsNullOrEmpty(response.Data.ProfilePictureUrl)
                    ? response.Data.ProfilePictureUrl
                    : response.Data.ProfilePicturePreviewUrl;

                LoadAvatar(avatarUrl);

                // Скрываем баджи по умолчанию
                SetBadges(null);
            }
            catch (Exception ex)
            {
                App.ErideMessage?.AddMessage($"Ошибка загрузки профиля пользователя {userId}: {ex.Message}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private void LoadAvatar(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
            {
                AvatarCachedImage.FileId = null;
                AvatarCachedImage.FileUrl = null;
                return;
            }

            var fileId = FileCacheService.ExtractFileIdFromUrl(avatarUrl);
            AvatarCachedImage.FileId = fileId;
            AvatarCachedImage.FileUrl = avatarUrl;
        }


        #endregion

        #region Attachment Tab Methods

        private void OnAttachmentTabChecked(object sender, RoutedEventArgs e)
        {
            // Проверяем, что все элементы контента инициализированы
            // (это событие может сработать до полной инициализации при IsChecked="True")
            if (ImagesContent == null || VideosContent == null || FilesContent == null || VoiceContent == null)
                return;

            // Скрываем все контенты
            ImagesContent.Visibility = Visibility.Collapsed;
            VideosContent.Visibility = Visibility.Collapsed;
            FilesContent.Visibility = Visibility.Collapsed;
            VoiceContent.Visibility = Visibility.Collapsed;

            // Показываем выбранный контент
            if (sender == ImagesTab)
                ImagesContent.Visibility = Visibility.Visible;
            else if (sender == VideosTab)
                VideosContent.Visibility = Visibility.Visible;
            else if (sender == FilesTab)
                FilesContent.Visibility = Visibility.Visible;
            else if (sender == VoiceTab)
                VoiceContent.Visibility = Visibility.Visible;
        }

        #endregion

        // Методы для установки данных пользователя (совместимость с существующим кодом)
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
                LoadAvatar(userProfile.AvatarPath);
            }

            // Установка баджей
            SetBadges(userProfile.Badges);
        }

        public void SetBadges(BadgeInfo[]? badges)
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
    }
}
