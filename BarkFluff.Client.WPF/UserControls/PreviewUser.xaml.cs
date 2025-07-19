using BarkFluff.Client.WPF.Pages.SetupPages;

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
                AvatarBrush.ImageSource = new BitmapImage(new Uri(avatarUrl));
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await App.ServerCommunication.ChangeBio(AboutTextBox.Text, App.GParam);
            Pattern?.NextStep();
        }
    }
}
