using BarkFluff.Client.WPF.Validators;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для PasswordReset.xaml
    /// </summary>
    public partial class PasswordReset : UserControl
    {
        private TextBox[] _codeBoxes = Array.Empty<TextBox>();
        private string _resetId = string.Empty;
        private string _userEmail = string.Empty;
        private string _userName = string.Empty;
        private bool _isProcessing;

        private static readonly Regex EmailPattern = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DigitOnlyPattern = new Regex(
            @"^\d$",
            RegexOptions.Compiled);

        public PasswordReset()
        {
            InitializeComponent();
            Loaded += PasswordReset_Loaded;
        }

        private void PasswordReset_Loaded(object sender, RoutedEventArgs e)
        {
            Step1Panel.Visibility = Visibility.Visible;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Collapsed;
            _codeBoxes = new[] { VerifyBox0, VerifyBox1, VerifyBox2, VerifyBox3, VerifyBox4, VerifyBox5 };
            EmailTextBox.Focus();
        }

        private void ShowStep2()
        {
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Visible;
            StepLine1.Fill = new SolidColorBrush(Color.FromRgb(109, 144, 243));
            Step2Indicator.Style = (Style)FindResource("ActiveStepIndicator");
            ClearVerificationCodeBoxes();
            VerifyBox0.Focus();
        }

        private void ShowStep3()
        {
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Visible;
            StepLine2.Fill = new SolidColorBrush(Color.FromRgb(109, 144, 243));
            Step3Indicator.Style = (Style)FindResource("ActiveStepIndicator");
            PasswordEnter.Focus();
        }

        private void ShowStep4()
        {
            Step3Panel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
            Step4Indicator.Style = (Style)FindResource("ActiveStepIndicator");
            StepLine3.Fill = new SolidColorBrush(Color.FromRgb(109, 144, 243));
            BackToLoginTextBlock.Visibility = Visibility.Collapsed;
        }

        private void BackToLogin_Click(object sender, MouseButtonEventArgs e)
        {
            App.MessengerWindow.OpenLoginPage();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            SendCodeButton.IsEnabled = enabled;
            VerifyCodeButton.IsEnabled = enabled;
            ResetPasswordButton.IsEnabled = enabled;
        }

        private bool IsEmailFormat(string input)
        {
            return EmailPattern.IsMatch(input);
        }

        private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            var inputText = EmailTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(inputText))
            {
                App.ErideMessage.AddMessage("Введите email или имя пользователя", new Erida { Type = MType.Warning });
                return;
            }

            _isProcessing = true;
            SetButtonsEnabled(false);

            try
            {
                var existEmail = await App.ServerCommunication.CheckEmail(inputText, App.GParam);
                var existLogin = await App.ServerCommunication.CheckUsername(inputText, App.GParam);

                if (!existEmail.error.IsSuccess || !existLogin.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage("Ошибка проверки пользователя. Пожалуйста, попробуйте еще раз.", new Erida { Type = MType.Error });
                    return;
                }

                if (existEmail.exists || existLogin.exists)
                {
                    _userEmail = string.Empty;
                    _userName = string.Empty;

                    if (IsEmailFormat(inputText))
                    {
                        _userEmail = inputText;
                    }
                    else
                    {
                        _userName = inputText;
                    }

                    var response = await App.ServerCommunication.ResetPassword(_userEmail, _userName, App.GParam);
                    if (!response.error.IsSuccess)
                    {
                        App.ErideMessage.AddMessage(response.error.ErrorMessage ?? "Неизвестная ошибка", new Erida { Type = MType.Error });
                        return;
                    }

                    _resetId = response.resetId ?? string.Empty;
                    ShowStep2();
                }
                else
                {
                    App.ErideMessage.AddMessage("Пользователь не найден", new Erida { Type = MType.Warning });
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка сети: {ex.Message}", new Erida { Type = MType.Error });
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private bool IsVerificationCodeComplete()
        {
            return _codeBoxes.All(b => b.Text.Length == 1);
        }

        private string GetVerificationCode()
        {
            return string.Concat(_codeBoxes.Select(b => b.Text));
        }

        private void ClearVerificationCodeBoxes()
        {
            foreach (var box in _codeBoxes)
            {
                box.Text = string.Empty;
            }
        }

        private async void VerifyCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            if (!IsVerificationCodeComplete())
            {
                App.ErideMessage.AddMessage("Введите полный 6-значный код", new Erida { Type = MType.Warning });
                return;
            }

            _isProcessing = true;
            SetButtonsEnabled(false);

            try
            {
                string code = GetVerificationCode();
                var response = await App.ServerCommunication.ConfirmResetCode(_resetId, code, App.GParam);

                if (response.refreshToken != null)
                {
                    App.GParam.RefreshToken = response.refreshToken;
                }
                MainWindow.SaveSettings();
                App.UpdateApiClient();
                await App.ServerCommunication.TokenUpdate(App.GParam);
                MainWindow.SaveSettings();

                ShowStep3();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(ex.Message, new Erida { Type = MType.Error });
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private void VerifyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox current) return;

            if (current.Text.Length == 1)
                current.Select(1, 0);
            else
                current.SelectAll();
        }

        private void VerifyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox current) return;

            if (current.Text.Length == 1)
            {
                int index = Array.IndexOf(_codeBoxes, current);
                if (index >= 0 && index < _codeBoxes.Length - 1)
                    _codeBoxes[index + 1].Focus();
                else
                    current.Select(1, 0);
            }
        }

        private void VerifyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox current) return;

            if (e.Key == Key.Back)
            {
                if (current.Text.Length == 0)
                {
                    int index = Array.IndexOf(_codeBoxes, current);
                    if (index > 0)
                    {
                        TextBox prev = _codeBoxes[index - 1];
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
            e.Handled = !DigitOnlyPattern.IsMatch(e.Text);
        }

        private async void ResendCode_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isProcessing) return;

            if (string.IsNullOrEmpty(_userEmail) && string.IsNullOrEmpty(_userName))
            {
                App.ErideMessage.AddMessage("Невозможно отправить код повторно. Начните заново.", new Erida { Type = MType.Error });
                return;
            }

            _isProcessing = true;
            SetButtonsEnabled(false);

            try
            {
                var response = await App.ServerCommunication.ResetPassword(_userEmail, _userName, App.GParam);
                if (!response.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage(response.error.ErrorMessage ?? "Ошибка отправки кода", new Erida { Type = MType.Error });
                    return;
                }

                _resetId = response.resetId ?? string.Empty;
                ClearVerificationCodeBoxes();
                VerifyBox0.Focus();
                App.ErideMessage.AddMessage("Код подтверждения отправлен на ваш email.", new Erida { Type = MType.Info });
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка сети: {ex.Message}", new Erida { Type = MType.Error });
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            // Validate password using existing validator
            if (!PasswordValidator.Validate(PasswordEnter.Password, out string passwordError))
            {
                App.ErideMessage.AddMessage(passwordError, new Erida { Type = MType.Warning });
                return;
            }

            // Validate passwords match
            if (!PasswordValidator.ValidateMatch(PasswordEnter.Password, PasswordRepeatedEnter.Password, out string matchError))
            {
                App.ErideMessage.AddMessage(matchError, new Erida { Type = MType.Warning });
                return;
            }

            _isProcessing = true;
            SetButtonsEnabled(false);

            try
            {
                var response = await App.ServerCommunication.SetPassword(PasswordEnter.Password, App.GParam);
                if (!response.IsSuccess)
                {
                    App.ErideMessage.AddMessage(response.ErrorMessage ?? "Неизвестная ошибка", new Erida { Type = MType.Error });
                    return;
                }

                ShowStep4();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка сети: {ex.Message}", new Erida { Type = MType.Error });
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            App.MessengerWindow.OpenLoginPage();
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            int strengthScore = Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password);
            PasswordStrengthBar.Value = strengthScore;

            var (message, colorHex) = Shared.SecurityUtilities.SecurityUtilities.GetPasswordStrengthMessage(strengthScore);
            PasswordDifficultyIndicator.Text = message;

            var brushConverter = new BrushConverter();
            if (brushConverter.ConvertFromString(colorHex) is Brush brush)
            {
                PasswordStrengthBar.Foreground = brush;
            }
        }

        private void NewPasswordBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }
    }
}
