using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.Client.WPF.Services.App.Caching;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для PreviewUser.xaml
    /// </summary>
    public partial class PreviewUser : UserControl
    {
        public string? fullName;
        public string? username;
        public string? avatarUrl;
        private string? _avatarFileId;

        public CreateAccount? Pattern;
        public PreviewUser()
        {
            InitializeComponent();
        }

        public void PreviewUser_Update(string fullName, string username, string avatarUrl)
        {
            FullNameText.Text = fullName;
            UsernameText.Text = username;
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                // Пытаемся извлечь fileId из URL
                _avatarFileId = FileCacheService.ExtractFileIdFromUrl(avatarUrl);

                // Используем кеш-сервис для загрузки аватара
                var imagePath = App.FileCacheService.GetCachedFilePath(_avatarFileId ?? string.Empty, FileType.Avatar, avatarUrl);
                SetAvatarImage(imagePath);

                // Подписываемся на событие кеширования файла
                App.FileCacheService.FileCached += OnFileCached;

                // Отписываемся при выгрузке контрола
                Unloaded += (s, e) =>
                {
                    App.FileCacheService.FileCached -= OnFileCached;
                };
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
                AvatarBrush.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
            }
            catch { }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await App.ServerCommunication.ChangeBio(AboutTextBox.Text, App.GParam);
            Pattern?.NextStep();
        }
    }
}
