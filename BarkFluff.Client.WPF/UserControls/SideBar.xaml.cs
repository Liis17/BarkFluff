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
        private string? _avatarFileId;

        public SideBar()
        {
            InitializeComponent();
            Loaded += SideBar_Loaded;
        }

        private void SideBar_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCurrentUserData();

            // Подписываемся на событие кеширования файла
            App.FileCacheService.FileCached += OnFileCached;

            // Отписываемся при выгрузке контрола
            Unloaded += (s, args) =>
            {
                App.FileCacheService.FileCached -= OnFileCached;
            };
        }

        private void LoadCurrentUserData()
        {
            if (App.GParam == null) return;

            // Устанавливаем имя пользователя и email
            UsernameText.Text = !string.IsNullOrEmpty(App.GParam.UserName) ? $"@{App.GParam.UserName}" : "username";
            EmailText.Text = App.GParam.FirstName ?? "email";

            // Загружаем аватар через кеш-сервис
            if (!string.IsNullOrEmpty(App.GParam.PictureUrl))
            {
                _avatarFileId = FileCacheService.ExtractFileIdFromUrl(App.GParam.PictureUrl);
                var imagePath = App.FileCacheService.GetCachedFilePath(_avatarFileId ?? string.Empty, FileType.Avatar, App.GParam.PictureUrl);
                SetAvatarImage(imagePath);
            }
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
                UserAvatarBrush.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
                BluredUserAvatarBrush.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        private void SidebarButtonClick(object sender, RoutedEventArgs e)
        {

        }
    }
}
