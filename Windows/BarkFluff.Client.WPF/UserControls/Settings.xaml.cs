using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls.SettingsPages;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для Settings.xaml
    /// </summary>
    public partial class Settings : UserControl
    {
        private readonly Dictionary<string, Func<BaseSettingsPage>> _factories;
        private readonly Dictionary<string, BaseSettingsPage> _pageCache = new();

        public Settings()
        {
            InitializeComponent();

            _factories = new Dictionary<string, Func<BaseSettingsPage>>
            {
                { "Profile", () => new ProfileSettingsPage() },
                { "Chats", () => new ChatsSettingsPage() },
                { "Notifications", () => new NotificationsSettingsPage() },
                { "Language", () => new LanguageSettingsPage() },
                { "Security", () => new SecuritySettingsPage() },
                { "Privacy", () => new PrivacySettingsPage() },
                { "Devices", () => new DevicesSettingsPage() },
                { "Cloud", () => new CloudSettingsPage() },
                { "Cache", () => new CacheSettingsPage() }
            };

            LoadSidebarProfile();

            // Автовыбор первого пункта
            Loaded += (_, _) =>
            {
                if (NavListBox.Items.Count > 0)
                    NavListBox.SelectedIndex = 0;
            };
        }

        private void LoadSidebarProfile()
        {
            var gp = App.GParam;
            if (gp == null) return;

            var displayName = $"{gp.FirstName} {gp.LastName}".Trim();
            SidebarDisplayName.Text = string.IsNullOrEmpty(displayName) ? gp.UserName : displayName;
            SidebarUsername.Text = $"@{gp.UserName}";

            LoadAvatar(gp.PictureUrl);
        }

        private void LoadAvatar(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
            {
                SidebarAvatar.FileId = null;
                SidebarAvatar.FileUrl = null;
                return;
            }

            var fileId = FileCacheService.ExtractFileIdFromUrl(avatarUrl);
            SidebarAvatar.FileId = fileId;
            SidebarAvatar.FileUrl = avatarUrl;
        }

        private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavListBox.SelectedItem is not ListBoxItem item)
                return;

            var pageKey = item.Tag as string;
            if (string.IsNullOrEmpty(pageKey) || !_factories.TryGetValue(pageKey, out var factory))
                return;

            if (!_pageCache.TryGetValue(pageKey, out var page))
            {
                page = factory();
                _pageCache[pageKey] = page;
            }

            page.OnNavigatedTo();

            // Fade-анимация при смене страницы
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            PageContentControl.Content = page;
            PageContentControl.BeginAnimation(OpacityProperty, fadeIn);
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
