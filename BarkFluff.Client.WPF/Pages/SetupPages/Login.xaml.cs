using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
