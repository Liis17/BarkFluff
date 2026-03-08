using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.Classes;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

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
            this.Unloaded += Profile_Unloaded;
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
            if (d is Profile control)
            {
                control.UpdateOnlineStatusDisplay();
            }
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
            {
                control.UpdateOnlineStatusDisplay();
            }
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

            // Загружаем баджи асинхронно (не блокируем UI)
            _ = LoadUserBadgesAsync(App.GParam.UserId);

            // ИСПРАВЛЕНИЕ: Подписываемся на онлайн-статус для себя
            _trackedUserId = App.GParam.UserId;
            App.OnlineStatusService.OnlineStatusChanged += OnOnlineStatusChanged;
            App.OnlineStatusService.TrackUser(App.GParam.UserId);

            // Получаем закешированный статус если доступен
            var cachedStatus = App.OnlineStatusService.GetCachedStatus(App.GParam.UserId);
            if (cachedStatus != null)
            {
                IsOnline = cachedStatus.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline;
                LastSeen = cachedStatus.LastSeen?.ToDateTime();
            }
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

                // Загружаем баджи асинхронно (не блокируем UI)
                _ = LoadUserBadgesAsync(userId);

                // Подписываемся на онлайн-статус
                _trackedUserId = userId;
                App.OnlineStatusService.OnlineStatusChanged += OnOnlineStatusChanged;
                App.OnlineStatusService.TrackUser(userId);

                // Получаем закешированный статус если доступен
                var cachedStatus = App.OnlineStatusService.GetCachedStatus(userId);
                if (cachedStatus != null)
                {
                    IsOnline = cachedStatus.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline;
                    LastSeen = cachedStatus.LastSeen?.ToDateTime();
                }
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

        /// <summary>
        /// Устанавливает отображение баджей пользователя
        /// </summary>
        /// <param name="badges">Массив баджей для отображения (до 3 штук)</param>
        private void SetBadges(BadgeInfo[]? badges)
        {
            // Сначала скрываем все баджи
            Badge1.Visibility = Visibility.Collapsed;
            Badge2.Visibility = Visibility.Collapsed;
            Badge3.Visibility = Visibility.Collapsed;
            BadgesContainer.Visibility = Visibility.Collapsed;

            if (badges == null || badges.Length == 0)
                return;

            // Показываем контейнер баджей
            BadgesContainer.Visibility = Visibility.Visible;

            // Показываем до 3 баджей
            for (int i = 0; i < Math.Min(badges.Length, 3); i++)
            {
                var badge = badges[i];

                switch (i)
                {
                    case 0:
                        Badge1.Visibility = Visibility.Visible;
                        Badge1Image.FileUrl = badge.ImageUrl;
                        Badge1Title.Text = badge.Name ?? string.Empty;
                        Badge1Description.Text = badge.Description ?? string.Empty;
                        break;
                    case 1:
                        Badge2.Visibility = Visibility.Visible;
                        Badge2Image.FileUrl = badge.ImageUrl;
                        Badge2Title.Text = badge.Name ?? string.Empty;
                        Badge2Description.Text = badge.Description ?? string.Empty;
                        break;
                    case 2:
                        Badge3.Visibility = Visibility.Visible;
                        Badge3Image.FileUrl = badge.ImageUrl;
                        Badge3Title.Text = badge.Name ?? string.Empty;
                        Badge3Description.Text = badge.Description ?? string.Empty;
                        break;
                }
            }
        }

        /// <summary>
        /// Загружает баджи пользователя с сервера
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        private async Task LoadUserBadgesAsync(long userId)
        {
            try
            {
                // Запрашиваем только топ-3 баджа для профиля
                var (error, protoBadges) = await App.ServerCommunication.GetUserBadges(App.GParam, userId, limit: 3);

                if (!error.IsSuccess || protoBadges == null || protoBadges.Count == 0)
                {
                    // Нет баджей или ошибка - скрываем секцию
                    Dispatcher.Invoke(() => SetBadges(null));
                    return;
                }

                // Конвертируем proto баджи в BadgeInfo
                var badgeInfos = protoBadges
                    .Select(ub => new BadgeInfo
                    {
                        ImageUrl = ub.Badge.ImageUrl,
                        Name = ub.Badge.Name,
                        Description = ub.Badge.Description,
                        Priority = ub.Priority
                    })
                    .ToArray();

                // Обновляем UI в главном потоке
                Dispatcher.Invoke(() => SetBadges(badgeInfos));
            }
            catch (Exception ex)
            {
                // В случае ошибки просто скрываем баджи (не критично для профиля)
                Dispatcher.Invoke(() => SetBadges(null));

                App.ErideMessage?.AddMessage($"Не удалось загрузить баджи пользователя {userId}: {ex.Message}",
                    new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
            }
        }

        #region Online Status

        private long _trackedUserId;

        private void UpdateOnlineStatusDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateOnlineStatusDisplay));
                return;
            }

            // Теперь гарантированно в UI потоке
            if (IsOnline)
            {
                // Кешируем предыдущие значения для минимизации мерцания
                if (OnlineStatusText.Text != "В сети")
                {
                    OnlineStatusText.Text = "В сети";
                    OnlineStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(68, 214, 44));
                }
                OnlineIndicator.Visibility = Visibility.Visible;
                OnlineStatusSection.Visibility = Visibility.Visible;
            }
            else if (LastSeen.HasValue)
            {
                var newText = FormatLastSeen(LastSeen.Value);
                if (OnlineStatusText.Text != newText)
                {
                    OnlineStatusText.Text = newText;
                    OnlineStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(128, 255, 255, 255));
                }
                OnlineIndicator.Visibility = Visibility.Collapsed;
                OnlineStatusSection.Visibility = Visibility.Visible;
            }
            else
            {
                OnlineStatusSection.Visibility = Visibility.Collapsed;
            }
        }

        private string FormatLastSeen(DateTime lastSeen)
        {
            // Проверяем если время Unknown (Unix epoch или очень старая дата)
            // gRPC Google.Protobuf.WellKnownTypes.Timestamp может вернуть 1970-01-01 для Unknown
            if (lastSeen.Year <= 1970 || lastSeen == DateTime.MinValue)
            {
                return "Был(а) давно";
            }

            // Конвертируем в локальное время для отображения
            var localTime = lastSeen.ToLocalTime();
            var now = DateTime.Now;

            // Если это сегодня - показываем только время
            if (localTime.Date == now.Date)
            {
                return $"Был(а) в {localTime:HH:mm}";
            }
            // Если это вчера
            else if (localTime.Date == now.Date.AddDays(-1))
            {
                return $"Был(а) вчера в {localTime:HH:mm}";
            }
            // Если это в пределах недели - показываем день недели
            else if ((now - localTime).TotalDays < 7)
            {
                string dayName = localTime.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
                return $"Был(а) в {dayName} в {localTime:HH:mm}";
            }
            // Старше недели - показываем дату
            else
            {
                return $"Был(а) {localTime:dd.MM.yyyy} в {localTime:HH:mm}";
            }
        }

        private void Profile_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_trackedUserId > 0)
            {
                App.OnlineStatusService.OnlineStatusChanged -= OnOnlineStatusChanged;
                App.OnlineStatusService.UntrackUser(_trackedUserId);
            }
        }

        private void OnOnlineStatusChanged(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // ВАЖНО: После исправления ProcessStatusUpdate это событие уже приходит в UI потоке,
            // но на всякий случай делаем дополнительную проверку
            if (status.UserId == _trackedUserId)
            {
                // Проверяем, нужен ли Dispatcher
                if (Dispatcher.CheckAccess())
                {
                    // Уже в UI потоке
                    IsOnline = status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline;
                    LastSeen = status.LastSeen?.ToDateTime();
                }
                else
                {
                    // Не в UI потоке - маршалируем
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsOnline = status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline;
                        LastSeen = status.LastSeen?.ToDateTime();
                    }));
                }
            }
        }

        #endregion
    }
}
