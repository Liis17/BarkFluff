using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

using Windows.Devices.Power;

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для PasswordReset.xaml
    /// </summary>
    public partial class PasswordReset : UserControl
    {
        private TextBox[]? codeBoxes;
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
            VerifyBox0.Focus();
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
                    // Отправка кода на почту
                    ShowStep2();
                }
                else
                {
                    MessageBox.Show("Пользователь не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }

            }
        }
        
        private void VerifyCodeButton_Click(object sender, RoutedEventArgs e)
        {
            
            
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

        private void ResendCode_Click(object sender, MouseButtonEventArgs e)
        {
            //повторная отправка кода
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
        private void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsValidPassword(PasswordEnter.Password) && Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password) >= 60 && PasswordEnter.Password == PasswordRepeatedEnter.Password)
            {
                ShowStep3();
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
