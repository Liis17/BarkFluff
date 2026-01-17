using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для SearchElement.xaml
    /// </summary>
    public partial class SearchElement : UserControl
    {
        private long _userId = 0;

        public SearchElement(UserData userData)
        {
            InitializeComponent();
            _userId = userData.Id;

            // Загружаем аватар через CachedAvatar контрол
            string avatarUrl = !string.IsNullOrEmpty(userData.ProfilePictureUrl)
                ? userData.ProfilePictureUrl
                : (!string.IsNullOrEmpty(userData.ProfilePicturePreviewUrl)
                    ? userData.ProfilePicturePreviewUrl
                    : string.Empty);

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                var fileId = FileCacheService.ExtractFileIdFromUrl(avatarUrl);
                AvatarCached.FileId = fileId;
                AvatarCached.FileUrl = avatarUrl;
            }

            UserName.Text = "@" + userData.Username;
            PublicName.Text = userData.FirstName + " " + userData.LastName;
        }

        private async void UserControl_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Messenger.OpenChatFromSearch(_userId, PublicName.Text);
        }
    }
}
