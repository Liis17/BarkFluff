using BarkFluff.Client.WPF.Services.App.Caching;

using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для Settings.xaml
    /// </summary>
    public partial class Settings : UserControl
    {
        public Settings()
        {
            InitializeComponent();
            LoadAvatar(App.GParam.PictureUrl);
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
    }
}
