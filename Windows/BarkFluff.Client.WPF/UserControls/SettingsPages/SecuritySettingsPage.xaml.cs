using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для SecuritySettingsPage.xaml
    /// </summary>
    public partial class SecuritySettingsPage : BaseSettingsPage
    {
        public override string Title => "Безопасность";

        private string? _resetId;

        public SecuritySettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            _ = RefreshTwoFaStatusAsync();
        }

        private async Task RefreshTwoFaStatusAsync()
        {
            try
            {
                var (error, authEnabled, _) = await App.ServerCommunication.OtpStatus(App.GParam);
                if (!error.IsSuccess) return;

                if (authEnabled)
                {
                    TwoFaStatusText.Text = "Включена";
                    SetupTwoFaButton.Visibility = Visibility.Collapsed;
                    ConfirmTwoFaButton.Visibility = Visibility.Collapsed;
                    QrImage.Visibility = Visibility.Collapsed;
                    OtpCodeBox.Visibility = Visibility.Collapsed;
                    DisableTwoFaButton.Visibility = Visibility.Visible;
                }
                else
                {
                    TwoFaStatusText.Text = "Отключена";
                    SetupTwoFaButton.Visibility = Visibility.Visible;
                    DisableTwoFaButton.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // ignored
            }
        }

        #region Смена пароля (3-шаговый flow)

        private async void RequestPasswordReset_Click(object sender, RoutedEventArgs e)
        {
            var gp = App.GParam;
            if (gp == null) return;

            StatusText.Text = "Отправка кода...";

            var (error, resetId) = await App.ServerCommunication.ResetPassword(
                gp.Email ?? "", gp.UserName ?? "", gp);

            if (error.IsSuccess && !string.IsNullOrEmpty(resetId))
            {
                _resetId = resetId;
                PasswordStep1.Visibility = Visibility.Collapsed;
                PasswordStep2.Visibility = Visibility.Visible;
                PasswordStepText.Text = "Введите 6-значный код, отправленный на вашу почту";
                StatusText.Text = "Код отправлен на почту";
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        private async void ConfirmResetCode_Click(object sender, RoutedEventArgs e)
        {
            var code = ResetCodeBox.Text?.Trim();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(_resetId))
            {
                StatusText.Text = "Введите код подтверждения";
                return;
            }

            StatusText.Text = "Проверка кода...";
            var (error, refreshToken) = await App.ServerCommunication.ConfirmResetCode(
                _resetId, code, App.GParam);

            if (error.IsSuccess)
            {
                PasswordStep2.Visibility = Visibility.Collapsed;
                PasswordStep3.Visibility = Visibility.Visible;
                PasswordStepText.Text = "Введите новый пароль";
                StatusText.Text = "Код подтверждён. Введите новый пароль";
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        private async void SetNewPassword_Click(object sender, RoutedEventArgs e)
        {
            var newPass = NewPasswordBox.Password;
            var confirmPass = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(newPass))
            {
                StatusText.Text = "Введите новый пароль";
                return;
            }

            if (newPass != confirmPass)
            {
                StatusText.Text = "Пароли не совпадают";
                return;
            }

            StatusText.Text = "Сохранение...";
            var error = await App.ServerCommunication.SetPassword(newPass, App.GParam);
            if (error.IsSuccess)
            {
                StatusText.Text = "Пароль успешно изменён";
                NewPasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                // Вернуть на шаг 1
                PasswordStep3.Visibility = Visibility.Collapsed;
                PasswordStep1.Visibility = Visibility.Visible;
                PasswordStepText.Text = "Для смены пароля на вашу почту будет отправлен код подтверждения";
                _resetId = null;
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        #endregion

        #region 2FA

        private async void SetupTwoFa_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Получение QR-кода...";
            var (error, qrBase64, justCode) = await App.ServerCommunication.OtpReceipt(App.GParam);
            if (!error.IsSuccess)
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
                return;
            }

            if (!string.IsNullOrEmpty(qrBase64))
            {
                try
                {
                    var imageBytes = Convert.FromBase64String(qrBase64);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(imageBytes);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    QrImage.Source = bitmap;
                    QrImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    StatusText.Text = "Не удалось отобразить QR-код";
                    return;
                }
            }

            OtpCodeBox.Visibility = Visibility.Visible;
            ConfirmTwoFaButton.Visibility = Visibility.Visible;
            SetupTwoFaButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Отсканируйте QR-код и введите код подтверждения";

            if (!string.IsNullOrEmpty(justCode))
            {
                TwoFaStatusText.Text = $"Или введите ключ вручную: {justCode}";
            }
        }

        private async void ConfirmTwoFa_Click(object sender, RoutedEventArgs e)
        {
            var code = OtpCodeBox.Text?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                StatusText.Text = "Введите код из приложения";
                return;
            }

            StatusText.Text = "Проверка...";
            var error = await App.ServerCommunication.OtpAccept(App.GParam, code);
            if (error.IsSuccess)
            {
                StatusText.Text = "2FA успешно настроена";
                QrImage.Visibility = Visibility.Collapsed;
                OtpCodeBox.Visibility = Visibility.Collapsed;
                ConfirmTwoFaButton.Visibility = Visibility.Collapsed;
                SetupTwoFaButton.Visibility = Visibility.Collapsed;
                DisableTwoFaButton.Visibility = Visibility.Visible;
                TwoFaStatusText.Text = "Включена";
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        private async void DisableTwoFa_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Отключение 2FA...";
            DisableTwoFaButton.IsEnabled = false;
            try
            {
                var error = await App.ServerCommunication.OtpDisable(App.GParam);
                if (error.IsSuccess)
                {
                    StatusText.Text = "2FA отключена";
                    DisableTwoFaButton.Visibility = Visibility.Collapsed;
                    SetupTwoFaButton.Visibility = Visibility.Visible;
                    TwoFaStatusText.Text = "Отключена";
                }
                else
                {
                    StatusText.Text = $"Ошибка: {error.ErrorMessage}";
                }
            }
            finally
            {
                DisableTwoFaButton.IsEnabled = true;
            }
        }

        #endregion

        #region PIN-код

        private void SavePin_Click(object sender, RoutedEventArgs e)
        {
            var pin = PinBox.Password?.Trim();
            if (App.GParam == null) return;

            if (string.IsNullOrEmpty(pin))
            {
                App.GParam.AppPass = string.Empty;
                StatusText.Text = "PIN-код удалён";
                return;
            }

            App.GParam.AppPass = pin;
            App.SaveGlobalParam();
            StatusText.Text = "PIN-код сохранён";
        }

        #endregion
    }
}
