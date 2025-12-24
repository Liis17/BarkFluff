using BarkFluff.Client.WPF.Services.App.Caching;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для SideBar.xaml
    /// </summary>
    public partial class SideBar : UserControl
    {
        public SideBar()
        {
            InitializeComponent();
            Loaded += SideBar_Loaded;
        }

        private void SideBar_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCurrentUserData();
#if (!DEBUG)
            DebugMenuButton.Visibility = Visibility.Hidden;
            DebugBorder.Visibility = Visibility.Hidden;
#else
            DebugMenuButton.Visibility = Visibility.Visible;
            DebugBorder.Visibility = Visibility.Visible;
#endif
        }

        private void LoadCurrentUserData()
        {
            if (App.GParam == null) return;

            // Устанавливаем имя пользователя и email
            UsernameText.Text = !string.IsNullOrEmpty(App.GParam.UserName) ? $"@{App.GParam.UserName}" : "email";
            var fnln = $"{App.GParam.FirstName} {App.GParam.LastName}".Trim();
            FullNameText.Text = fnln ?? "first name last name";

            // Загружаем аватар через CachedAvatar контрол
            if (!string.IsNullOrEmpty(App.GParam.PictureUrl))
            {
                var fileId = FileCacheService.ExtractFileIdFromUrl(App.GParam.PictureUrl);
                UserAvatarCached.FileId = fileId;
                UserAvatarCached.FileUrl = App.GParam.PictureUrl;

                // Также обновляем размытый фон (это остаётся отдельным)
                SetBlurredBackground(App.GParam.PictureUrl);
            }
        }

        private void SetBlurredBackground(string pictureUrl)
        {
            try
            {
                var fileId = FileCacheService.ExtractFileIdFromUrl(pictureUrl);
                var imagePath = App.FileCacheService.GetCachedFilePath(fileId ?? string.Empty, FileType.Avatar, pictureUrl);
                BluredUserAvatarBrush.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        private void SidebarButtonClick(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Открытие чата с самим собой (избранное)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFavorites(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Обрабатывает событие Click кнопки «Открыть настройки» для отображения диалогового окна настроек приложения.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            App.Messenger.ClosePanel();
            App.Messenger.OpenSettings();
        }

        /// <summary>
        /// Открытие отладочного меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenDebugMenuButtonClick(object sender, RoutedEventArgs e)
        {
            App.Messenger.ClosePanel();
            App.Messenger.OpenDebugMenu();
        }
    }
}
