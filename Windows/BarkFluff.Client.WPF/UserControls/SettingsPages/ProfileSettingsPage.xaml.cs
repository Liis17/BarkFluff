using BarkFluff.Client.WPF.Services.App.Caching;

using Microsoft.Win32;

using System.IO;
using System.Windows;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для ProfileSettingsPage.xaml
    /// </summary>
    public partial class ProfileSettingsPage : BaseSettingsPage
    {
        public override string TitleKey => "L_Settings_Sidebar_Profile";

        public ProfileSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadProfileData();
        }

        private async void LoadProfileData()
        {
            var gp = App.GParam;
            if (gp == null) return;

            try
            {
                var (error, userData) = await App.ServerCommunication.GetUserData(gp);
                if (error.IsSuccess && userData != null)
                {
                    FirstNameBox.Text = userData.FirstName;
                    LastNameBox.Text = userData.LastName;
                    UsernameBox.Text = userData.Username;
                    BioBox.Text = userData.Description;
                    AvatarNameText.Text = $"{userData.FirstName} {userData.LastName}".Trim();
                    AvatarEmailText.Text = gp.Email ?? "";

                    if (!string.IsNullOrEmpty(userData.ProfilePictureUrl))
                    {
                        var fileId = FileCacheService.ExtractFileIdFromUrl(userData.ProfilePictureUrl);
                        ProfileAvatar.FileId = fileId;
                        ProfileAvatar.FileUrl = userData.ProfilePictureUrl;
                    }
                }
            }
            catch
            {
                // Fallback к локальным данным
                FirstNameBox.Text = gp.FirstName;
                LastNameBox.Text = gp.LastName;
                UsernameBox.Text = gp.UserName;
                BioBox.Text = gp.Description;
                AvatarNameText.Text = $"{gp.FirstName} {gp.LastName}".Trim();
                AvatarEmailText.Text = gp.Email ?? "";
            }
        }

        private async void SaveName_Click(object sender, RoutedEventArgs e)
        {
            // В API нет отдельного метода для смены имени/фамилии — используем ChangeBio
            // или ChangeUsername, если имя связано с username.
            // Здесь сохраняем локально — при наличии API для имени добавить вызов.
            if (App.GParam != null)
            {
                App.GParam.FirstName = FirstNameBox.Text;
                App.GParam.LastName = LastNameBox.Text;
                AvatarNameText.Text = $"{FirstNameBox.Text} {LastNameBox.Text}".Trim();
                StatusText.Text = L("L_Settings_Profile_NameUpdated");
            }
        }

        private async void SaveUsername_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text?.Trim();
            if (string.IsNullOrEmpty(username)) return;

            StatusText.Text = L("L_Common_Saving");
            var error = await App.ServerCommunication.ChangeUsername(username, App.GParam);
            if (error.IsSuccess)
            {
                App.GParam.UserName = username;
                StatusText.Text = L("L_Settings_Profile_UsernameUpdated");
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        private async void SaveBio_Click(object sender, RoutedEventArgs e)
        {
            var bio = BioBox.Text ?? "";

            StatusText.Text = L("L_Common_Saving");
            var error = await App.ServerCommunication.ChangeBio(bio, App.GParam);
            if (error.IsSuccess)
            {
                App.GParam.Description = bio;
                StatusText.Text = L("L_Settings_Profile_BioUpdated");
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        private async void ChangeAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = L("L_Settings_Profile_AvatarDialog_Filter"),
                Title = L("L_Settings_Profile_AvatarDialog_Title")
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                StatusText.Text = L("L_Settings_Profile_UploadingAvatar");
                var bytes = await File.ReadAllBytesAsync(dialog.FileName);
                var error = await App.ServerCommunication.UploadUserAvatarAsync(App.GParam, bytes);
                if (error.IsSuccess)
                {
                    StatusText.Text = L("L_Settings_Profile_AvatarUpdated");
                }
                else
                {
                    StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = L("L_Common_Error_Prefix") + ex.Message;
            }
        }

        private static string L(string key)
            => Application.Current?.TryFindResource(key) as string ?? key;
    }
}
