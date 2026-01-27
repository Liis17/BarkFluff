using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BarkFluff.Client.WPF.UserControls.SettingsPages;
using BarkFluff.Client.WPF.Services.App.Caching;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для Settings.xaml
    /// </summary>
    public partial class Settings : UserControl
    {
        private BaseSettingsPage? _currentPage;
        private readonly Dictionary<string, Func<BaseSettingsPage>> _pages;

        public ICommand NavigateCommand { get; }

        public Settings()
        {
            InitializeComponent();
            DataContext = this;
            LoadAvatar(App.GParam.PictureUrl);

            // Регистрация страниц настроек
            _pages = new Dictionary<string, Func<BaseSettingsPage>>
            {
                { "Chats", () => new SettingsPages.ChatsSettingsPage() },
                { "Profile", () => new SettingsPages.ProfileSettingsPage() },
                { "Language", () => new SettingsPages.LanguageSettingsPage() },
                { "Notifications", () => new SettingsPages.NotificationsSettingsPage() },
                { "Security", () => new SettingsPages.SecuritySettingsPage() },
                { "Privacy", () => new SettingsPages.PrivacySettingsPage() },
                { "Devices", () => new SettingsPages.DevicesSettingsPage() },
                { "Cache", () => new SettingsPages.CacheSettingsPage() },
                { "Cloud", () => new SettingsPages.CloudSettingsPage() }
            };

            NavigateCommand = new RelayCommand<string>(NavigateTo);
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

        /// <summary>
        /// Навигация к странице настроек с анимацией.
        /// </summary>
        private void NavigateTo(string pageKey)
        {
            if (!_pages.TryGetValue(pageKey, out var pageFactory))
            {
                return;
            }

            var newPage = pageFactory();
            newPage.BackRequested += (s, e) => GoBack();

            _currentPage = newPage;
            PageContentControl.Content = newPage;

            // Анимация перехода
            AnimateTransition(true);
        }

        /// <summary>
        /// Возврат к главному меню с анимацией.
        /// </summary>
        private void GoBack()
        {
            AnimateTransition(false);
        }

        /// <summary>
        /// Анимация перехода между меню и страницей.
        /// </summary>
        /// <param name="navigateForward">true - переход к странице, false - возврат к меню</param>
        private void AnimateTransition(bool navigateForward)
        {
            const double offset = 500;
            const int durationMs = 300;

            FrameworkElement currentContent = navigateForward ? MainMenuPanel : PageContainer;
            FrameworkElement newContent = navigateForward ? PageContainer : MainMenuPanel;

            // Настраиваем Transform для анимации
            currentContent.RenderTransform = new TranslateTransform();
            newContent.RenderTransform = new TranslateTransform();

            // Показываем новый контейнер
            newContent.Visibility = Visibility.Visible;

            // Создаем Storyboard для анимации
            var storyboard = new Storyboard();

            // Анимация ухода текущего контента
            var currentAnimation = new DoubleAnimation
            {
                To = navigateForward ? -offset : offset,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(currentAnimation, currentContent);
            Storyboard.SetTargetProperty(currentAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            storyboard.Children.Add(currentAnimation);

            // Анимация появления нового контента
            var newAnimation = new DoubleAnimation
            {
                From = navigateForward ? offset : -offset,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(newAnimation, newContent);
            Storyboard.SetTargetProperty(newAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            storyboard.Children.Add(newAnimation);

            // После завершения анимации скрываем ушедший контент
            storyboard.Completed += (s, e) =>
            {
                currentContent.Visibility = Visibility.Collapsed;
                currentContent.RenderTransform = null;
                newContent.RenderTransform = null;

                if (!navigateForward)
                {
                    _currentPage = null;
                    PageContentControl.Content = null;
                }
            };

            storyboard.Begin();
        }
    }

    /// <summary>
    /// Простая реализация ICommand для навигации.
    /// </summary>
    internal class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
    }

    /// <summary>
    /// Generic RelayCommand with type parameter.
    /// </summary>
    internal class RelayCommand<T> : RelayCommand
    {
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
            : base(
                parameter => execute(parameter is T t ? t : default),
                parameter => canExecute == null || (parameter is T t && canExecute(t)))
        {
        }
    }
}
