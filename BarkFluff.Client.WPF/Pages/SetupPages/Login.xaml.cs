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

        private bool _step2FA = false;

        private TextBox[]? codeBoxes;
        public Login()
        {
            InitializeComponent();
            Loaded += Login_Loaded;
        }

        private void Login_Loaded(object sender, RoutedEventArgs e)
        {
            OtpBlock.Visibility = Visibility.Collapsed;
            codeBoxes = new[] { VerifyBox0, VerifyBox1, VerifyBox2, VerifyBox3, VerifyBox4, VerifyBox5 };
            VerifyBox0.Focus();
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
                var response = await App.ServerCommunication.Authorisation(_email, _username, _password, _otpCode, App.GParam);
                if (response.getMeOtpCode)
                {
                    _step2FA = true;
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


        private void VerifyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox current = sender as TextBox;
            if (current.Text.Length == 1)
                current.Select(1, 0); // Чтобы курсор не прыгал
            else
                current.SelectAll();
        }
        private async void VerifyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox current = sender as TextBox;
            if (current.Text.Length == 1)
            {
                int index = Array.IndexOf(codeBoxes, current);
                if (index < codeBoxes.Length - 1)
                    codeBoxes[index + 1].Focus();
                else
                    current.Select(1, 0); // Не прыгать в конец
            }

            if (codeBoxes.All(b => b.Text.Length == 1))
            {
                string code = string.Concat(codeBoxes.Select(b => b.Text));
                try
                {

                    await App.ServerCommunication.OtpAccept(App.GParam, code); // Отправка кода на сервер

                }
                catch
                {

                    return;
                }

            }
        }
        private void VerifyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox current = sender as TextBox;

            if (e.Key == Key.Back)
            {
                if (current.Text.Length == 0)
                {
                    int index = Array.IndexOf(codeBoxes, current);
                    if (index > 0)
                    {
                        TextBox prev = codeBoxes[index - 1];
                        prev.Focus();
                        prev.SelectAll();
                    }
                }
            }

            if (e.Key == Key.Tab)
                e.Handled = true;
        }
        private void VerifyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }
    }
}
