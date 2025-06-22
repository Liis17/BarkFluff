using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        private string _username = string.Empty;
        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _otpCode = string.Empty;
        public Login()
        {
            InitializeComponent();
        }

        private void CreateAccountPageOpen(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreateAccountPage();
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            var existEmail = await App.ServerCommunication.CheckEmail(UsernameTextBox.Text, App.GParam);
            var existLogin = await App.ServerCommunication.CheckUsername(UsernameTextBox.Text, App.GParam);
            if (existEmail || existLogin)
            {

                bool ContainsEmail(string input)
                {
                    string pattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
                    return Regex.IsMatch(input, pattern);
                }

                if (ContainsEmail(UsernameTextBox.Text))
                {
                    _email = UsernameTextBox.Text;
                }
                else
                {
                    _username = UsernameTextBox.Text;
                }
                _password = PasswordBox.Password;
                var response = await App.ServerCommunication.Authorisation(_email, _username, _password, "", App.GParam);
                if (response.getMeOtpCode)
                {
                    LoginPasswordFields.Visibility = Visibility.Collapsed;
                    OtpBlock.Visibility = Visibility.Visible;
                }
                else if (!response.Item1)
                {
                    MessageBox.Show(response.error, "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Имя пользователя/почта или пароль содержат ошибку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }




            
        }

        private void TwoFABox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {

        }

        private void TwoFABox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void BackToServerList(object sender, MouseButtonEventArgs e)
        {
            App.MessengerWindow.OpenServerListPage();
        }

        private void ResetPasswordClick(object sender, MouseButtonEventArgs e)
        {
            App.MessengerWindow.OpenPasswordRecoveryPage();
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show("Раздел помощи в разработке, не скучайте )", "Информация", MessageBoxButton.AbortRetryIgnore, MessageBoxImage.Information);
        }
    }
}
