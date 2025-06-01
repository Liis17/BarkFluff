using BarkFluff.Client.WPF.Pages.SetupPages;

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
    /// Логика взаимодействия для PreviewUser.xaml
    /// </summary>
    public partial class PreviewUser : UserControl
    {
        public string? fullName;
        public string? username;
        public string? avatarUrl;

        public CreateAccount Pattern;
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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            //вызвать отправку био на сервер 
            Pattern.Cropping();
        }

        private void AboutTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
