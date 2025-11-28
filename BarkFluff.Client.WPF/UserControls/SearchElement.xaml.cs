using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для SearchElement.xaml
    /// </summary>
    public partial class SearchElement : UserControl
    {
        private long _userId = 0;
        private string? _avatarFileId;

        public SearchElement(UserData userData)
        {
            InitializeComponent();
            _userId = userData.Id;

            // Загружаем аватар через кеш-сервис
            string avatarUrl = !string.IsNullOrEmpty(userData.ProfilePictureUrl)
                ? userData.ProfilePictureUrl
                : (!string.IsNullOrEmpty(userData.ProfilePicturePreviewUrl)
                    ? userData.ProfilePicturePreviewUrl
                    : string.Empty);

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                _avatarFileId = ExtractFileIdFromUrl(avatarUrl);
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
            else
            {
                AvatarImage.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/Images/barkfluff_logo.png"));
            }

            UserName.Text = "@" + userData.Username;
            PublicName.Text = userData.FirstName + " " + userData.LastName;
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
                AvatarImage.ImageSource = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
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

        private void UserControl_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Messenger.ChatIdbyUserId.Value = _userId;
        }
    }
}
