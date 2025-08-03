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
        public SearchElement(UserData userData)
        {
            InitializeComponent();
            if (userData.ProfilePictureUrl != string.Empty)
            {
                AvatarImage.ImageSource = new BitmapImage(new Uri(userData.ProfilePictureUrl));
            }
            else
            {
                AvatarImage.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/Images/barkfluff_logo.png"));
            }

            UserName.Text = "@" + userData.Username;
            PublicName.Text = userData.FirstName + " " + userData.LastName;
        }
    }
}
