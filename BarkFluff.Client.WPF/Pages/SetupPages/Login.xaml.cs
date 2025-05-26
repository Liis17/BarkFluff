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

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    /// <summary>
    /// Логика взаимодействия для Login.xaml
    /// </summary>
    public partial class Login : UserControl
    {
        public Login()
        {
            InitializeComponent();
        }

        private void CreateAccountPageOpen(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreateAccountPage();
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void TwoFABox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {

        }

        private void TwoFABox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
