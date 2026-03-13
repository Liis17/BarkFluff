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
        public override string Title => "Защита";

        public SecuritySettingsPage()
        {
            InitializeComponent();
        }

        private async void ChangePassword_Click(object sender, RoutedEventArgs e)
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
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

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
                SetupTwoFaButton.Visibility = Visibility.Visible;
                TwoFaStatusText.Text = "Включена";
            }
            else
            {
                StatusText.Text = $"Ошибка: {error.ErrorMessage}";
            }
        }

        private void SavePin_Click(object sender, RoutedEventArgs e)
        {
            var pin = PinBox.Password?.Trim();
            if (string.IsNullOrEmpty(pin))
            {
                if (App.GParam != null)
                {
                    App.GParam.AppPass = null;
                    StatusText.Text = "PIN-код удалён";
                }
                return;
            }

            if (App.GParam != null)
            {
                App.GParam.AppPass = pin;
                StatusText.Text = "PIN-код сохранён";
            }
        }
    }
}
