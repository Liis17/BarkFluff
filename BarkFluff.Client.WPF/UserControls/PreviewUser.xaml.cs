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
                _avatarFileId = ExtractFileIdFromUrl(avatarUrl);

                // Используем кеш-сервис для загрузки аватара
                var imagePath = App.FileCacheService.GetCachedFilePath(_avatarFileId ?? string.Empty, FileType.Avatar, avatarUrl);
                SetAvatarImage(imagePath);

                // Подписываемся на событие кеширования файла
                App.FileCacheService.FileCached += OnFileCached;
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

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await App.ServerCommunication.ChangeBio(AboutTextBox.Text, App.GParam);
            Pattern?.NextStep();
        }
    }
}
