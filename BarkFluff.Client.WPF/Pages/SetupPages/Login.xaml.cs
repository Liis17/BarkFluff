using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
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
        private bool _isLoading = false;

        private TextBox[]? codeBoxes;

        // Regex patterns for validation
        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);
        private static readonly Regex UsernameRegex = new Regex(
            @"^[a-zA-Z0-9._]{3,}$",
            RegexOptions.Compiled);

        public Login()
        {
            InitializeComponent();
            Loaded += Login_Loaded;
        }

        private void Login_Loaded(object sender, RoutedEventArgs e)
        {
            OtpBlock.Visibility = Visibility.Collapsed;
            codeBoxes = new[] { VerifyBox0, VerifyBox1, VerifyBox2, VerifyBox3, VerifyBox4, VerifyBox5 };
            UsernameTextBox.Focus();

            // Fixed: BeaconIsnull logic was inverted - now checks correctly
            if (!App.ServerCommunication.BeaconIsnull)
            {
                App.ErideMessage.AddMessage("Beacon API клиент успешно инициализирован.", new Erida { Type = MType.Debug });
            }
            else
            {
                App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);
                App.ErideMessage.AddMessage("Ошибка инициализации Beacon API клиента, попытка переподключения.", new Erida { Type = MType.Error });
            }
        }

        private void CreateAccountPageOpen(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenCreateAccountPage();
        }

        #region Validation Methods

        private bool ValidateUsername()
        {
            var input = UsernameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                ShowError(UsernameTextBox, UsernameErrorText, "Введите имя пользователя или email");
                return false;
            }

            if (input.Length > 50)
            {
                ShowError(UsernameTextBox, UsernameErrorText, "Максимальная длина 50 символов");
                return false;
            }

            // Check if it's an email
            if (input.Contains('@'))
            {
                if (!EmailRegex.IsMatch(input))
                {
                    ShowError(UsernameTextBox, UsernameErrorText, "Некорректный формат email");
                    return false;
                }
            }
            else
            {
                // It's a username
                if (input.Length < 3)
                {
                    ShowError(UsernameTextBox, UsernameErrorText, "Минимум 3 символа");
                    return false;
                }
                if (!UsernameRegex.IsMatch(input))
                {
                    ShowError(UsernameTextBox, UsernameErrorText, "Только буквы, цифры, точки и подчёркивания");
                    return false;
                }
            }

            HideError(UsernameTextBox, UsernameErrorText);
            return true;
        }

        private bool ValidatePassword()
        {
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError(PasswordBox, PasswordErrorText, "Введите пароль");
                return false;
            }

            if (password.Length < 6)
            {
                ShowError(PasswordBox, PasswordErrorText, "Минимальная длина 6 символов");
                return false;
            }

            HideError(PasswordBox, PasswordErrorText);
            return true;
        }

        private bool ValidateOtp()
        {
            if (codeBoxes == null)
            {
                return false;
            }

            if (!codeBoxes.All(b => b.Text.Length == 1))
            {
                ShowOtpError("Заполните все 6 полей");
                return false;
            }

            HideOtpError();
            return true;
        }

        private void ShowError(Control control, TextBlock errorText, string message)
        {
            if (control is TextBox textBox)
            {
                textBox.Style = (Style)FindResource("MinimalTextBoxError");
            }
            else if (control is PasswordBox passwordBox)
            {
                passwordBox.Style = (Style)FindResource("MinimalPasswordBoxError");
            }
            errorText.Text = message;
            errorText.Visibility = Visibility.Visible;
        }

        private void HideError(Control control, TextBlock errorText)
        {
            if (control is TextBox textBox)
            {
                textBox.Style = (Style)FindResource("MinimalTextBox");
            }
            else if (control is PasswordBox passwordBox)
            {
                passwordBox.Style = (Style)FindResource("MinimalPasswordBox");
            }
            errorText.Visibility = Visibility.Collapsed;
        }

        private void ShowOtpError(string message)
        {
            if (codeBoxes != null)
            {
                foreach (var box in codeBoxes)
                {
                    box.Style = (Style)FindResource("VerificationTextBoxError");
                }
            }
            OtpErrorText.Text = message;
            OtpErrorText.Visibility = Visibility.Visible;
        }

        private void HideOtpError()
        {
            if (codeBoxes != null)
            {
                foreach (var box in codeBoxes)
                {
                    box.Style = (Style)FindResource("VerificationTextBox");
                }
            }
            OtpErrorText.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region LostFocus Event Handlers

        private void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                ValidateUsername();
            }
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                ValidatePassword();
            }
        }

        #endregion

        #region Loading State Methods

        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            SignInButton.IsEnabled = !isLoading;
            SignInButtonContent.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
            SignInLoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Cleanup Methods

        private void ClearSensitiveData()
        {
            _password = string.Empty;
            _otpCode = string.Empty;
        }

        private void ClearPasswordAndOtp()
        {
            PasswordBox.Clear();
            if (codeBoxes != null)
            {
                foreach (var box in codeBoxes)
                {
                    box.Clear();
                }
            }
        }

        #endregion

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent duplicate clicks
            if (_isLoading)
            {
                return;
            }

            // Validate input for login/password step
            if (!_step2FA)
            {
                if (!ValidateUsername() || !ValidatePassword())
                {
                    return;
                }
            }
            else
            {
                // Validate OTP
                if (!ValidateOtp())
                {
                    return;
                }
            }

            SetLoadingState(true);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var existEmail = await App.ServerCommunication.CheckEmail(UsernameTextBox.Text, App.GParam);
                var existLogin = await App.ServerCommunication.CheckUsername(UsernameTextBox.Text, App.GParam);

                if (!existEmail.error.IsSuccess && !existLogin.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage("Ошибка проверки имени пользователя или почты", new Erida { Type = MType.Error });
                    ClearPasswordAndOtp();
                    return;
                }

                if (existEmail.exists || existLogin.exists)
                {
                    bool ContainsEmail(string input)
                    {
                        return EmailRegex.IsMatch(input);
                    }

                    if (ContainsEmail(UsernameTextBox.Text))
                    {
                        _email = UsernameTextBox.Text;
                        _username = string.Empty;
                    }
                    else
                    {
                        _username = UsernameTextBox.Text;
                        _email = string.Empty;
                    }

                    if (codeBoxes != null && codeBoxes.All(b => b.Text.Length == 1))
                    {
                        _otpCode = string.Concat(codeBoxes.Select(b => b.Text));
                    }

                    _password = PasswordBox.Password;
                    var response = await App.ServerCommunication.Authorizations(_email, _username, _password, _otpCode, App.GParam);

                    // Clear sensitive data after use
                    ClearSensitiveData();

                    if (!response.Error.IsSuccess && !response.getMeOtpCode)
                    {
                        App.ErideMessage.AddMessage(response.Error.ErrorMessage, new Erida { Type = MType.Error });
                        ClearPasswordAndOtp();
                        return;
                    }
                    else if (response.getMeOtpCode)
                    {
                        _step2FA = true;
                        LoginPasswordFields.Visibility = Visibility.Collapsed;
                        OtpBlock.Visibility = Visibility.Visible;
                        App.ErideMessage.AddMessage("Введите код двухфакторной аутентификации", new Erida { Type = MType.Info });
                        VerifyBox0.Focus();
                        return;
                    }

                    App.GParam.RefreshToken = response.refreshToken;
                    App.GParam.AccessToken = response.accessToken;

                    var responseUserData = await App.ServerCommunication.GetUserData(App.GParam);

                    App.GParam.UserId = responseUserData.Data.Id;
                    App.GParam.UserName = responseUserData.Data.Username;
                    App.GParam.FirstName = responseUserData.Data.FirstName;
                    App.GParam.LastName = responseUserData.Data.LastName;
                    App.GParam.Description = responseUserData.Data.Description;
                    MainWindow.SaveSettings();

                    App.OpenMessengerPage();
                }
                else
                {
                    App.ErideMessage.AddMessage("Имя пользователя/почта или пароль содержат ошибку", new Erida { Type = MType.Error });
                    ClearPasswordAndOtp();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                App.ErideMessage.AddMessage("Время ожидания запроса истекло. Попробуйте позже.", new Erida { Type = MType.Error });
                ClearPasswordAndOtp();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка сети: {ex.Message}", new Erida { Type = MType.Error });
                ClearPasswordAndOtp();
            }
            finally
            {
                SetLoadingState(false);
                ClearSensitiveData();
            }
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
            App.ErideMessage.AddMessage("Раздел помощи в разработке, не скучайте )", new Erida { Type = MType.Info });
        }


        private void VerifyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox current)
            {
                if (current.Text.Length == 1)
                    current.Select(1, 0); // Чтобы курсор не прыгал
                else
                    current.SelectAll();
            }
        }

        private void VerifyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox current && codeBoxes != null)
            {
                if (current.Text.Length == 1)
                {
                    int index = Array.IndexOf(codeBoxes, current);
                    if (index >= 0 && index < codeBoxes.Length - 1)
                        codeBoxes[index + 1].Focus();
                    else
                        current.Select(1, 0); // Не прыгать в конец
                }
            }
        }

        private void VerifyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox current && codeBoxes != null)
            {
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
        }

        private void VerifyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }
    }
}
