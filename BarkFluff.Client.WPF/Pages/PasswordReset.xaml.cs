using BarkFluff.WebApi.Core.MessengerData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

using Windows.Devices.Power;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для PasswordReset.xaml
    /// </summary>
    public partial class PasswordReset : UserControl
    {
        private TextBox[]? codeBoxes;
        private string _resetId = string.Empty;
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
            codeBoxes = new[] { VerifyBox0, VerifyBox1, VerifyBox2, VerifyBox3, VerifyBox4, VerifyBox5 };
            EmailTextBox.Focus();
        }

        private void ShowStep2()
        {
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Visible;
            StepLine1.Fill = new SolidColorBrush(Color.FromRgb(109, 144, 243));
            Step2Indicator.Style = (Style)FindResource("ActiveStepIndicator");
        }

        private void ShowStep3()
        {
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Visible;
            StepLine2.Fill = new SolidColorBrush(Color.FromRgb(109, 144, 243));
            Step3Indicator.Style = (Style)FindResource("ActiveStepIndicator");
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

        private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(EmailTextBox.Text))
            {
                var existEmail = await App.ServerCommunication.CheckEmail(EmailTextBox.Text, App.GParam);
                var existLogin = await App.ServerCommunication.CheckUsername(EmailTextBox.Text, App.GParam);
                if (existEmail || existLogin)
                {

                    bool ContainsEmail(string input)
                    {
                        string pattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
                        return Regex.IsMatch(input, pattern);
                    }

                    string _email = string.Empty;
                    string _username = string.Empty;
                    if (ContainsEmail(EmailTextBox.Text))
                    {
                        _email = EmailTextBox.Text;
                    }
                    else
                    {
                        _username = EmailTextBox.Text;
                    }
                    var response = await App.ServerCommunication.ResetPassword(_email, _username, App.GParam);
                    if (!response.Item1) { return; } 
                    _resetId = response.resetId; 
                    ShowStep2();
                }
                else
                {
                    MessageBox.Show("Пользователь не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }

            }
        }
        
        private async void VerifyCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (codeBoxes.All(b => b.Text.Length == 1))
            {
                string code = string.Concat(codeBoxes.Select(b => b.Text));
                //MessageBox.Show(code);
                var response = await App.ServerCommunication.ConfirmResetCode(_resetId, code , App.GParam);
                App.GParam.RefreshToken = response.refreshToken;
                MainWindow.SaveSettings();
                App.UpdateApiClient();
                await App.ServerCommunication.TokenUpdate(App.GParam);
                MainWindow.SaveSettings();

                ShowStep3();
            }
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

        private void ResendCode_Click(object sender, MouseButtonEventArgs e)
        {
            //повторная отправка кода
            MessageBox.Show("Пока что это не реализовано, увы", "Повторная отправка кода которая не работает", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
        }
        public bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Пароль не должен быть пустым.");
                return false;
            }

            if (password.Length < 8)
            {
                MessageBox.Show("Пароль должен содержать не менее 8 символов.");
                return false;
            }

            if (password.Contains(" "))
            {
                MessageBox.Show("Пароль не должен содержать пробелы.");
                return false;
            }
            return true;
        }
        private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsValidPassword(PasswordEnter.Password) && Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password) >= 60 && PasswordEnter.Password == PasswordRepeatedEnter.Password)
            {
                var response = await App.ServerCommunication.SetPassword(PasswordEnter.Password, App.GParam);
                if (!response)
                {
                    MessageBox.Show("Не удалось установить новый пароль. Пожалуйста, попробуйте еще раз.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                ShowStep4();
            }
            else
            {
                MessageBox.Show("Пароли не совпадают или не достаточно сильные.");
            }
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            
            App.MessengerWindow.OpenLoginPage();
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var a = 0;
            PasswordStrengthBar.Value = a = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password);
            var colors = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.GetPasswordStrengthMessage(a);
            PasswordDifficultyIndicator.Text = colors.message;
            PasswordStrengthBar.Foreground = (Brush)new BrushConverter().ConvertFromString(colors.colorHex);
        }

        private void NewPasswordBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
