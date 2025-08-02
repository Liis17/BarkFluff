using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
