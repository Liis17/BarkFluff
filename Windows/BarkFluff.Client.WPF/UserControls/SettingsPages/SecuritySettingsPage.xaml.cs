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
        public override string TitleKey => "L_Settings_Security_Title";

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
                    TwoFaStatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_TwoFa_Enabled");
                    SetupTwoFaButton.Visibility = Visibility.Collapsed;
                    ConfirmTwoFaButton.Visibility = Visibility.Collapsed;
                    QrImage.Visibility = Visibility.Collapsed;
                    OtpCodeBox.Visibility = Visibility.Collapsed;
                    DisableTwoFaButton.Visibility = Visibility.Visible;
                }
                else
                {
                    TwoFaStatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_TwoFa_Disabled");
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

            StatusText.Text = L("L_Settings_Security_Password_SendingCode");

            var (error, resetId) = await App.ServerCommunication.ResetPassword(
                gp.Email ?? "", gp.UserName ?? "", gp);

            if (error.IsSuccess && !string.IsNullOrEmpty(resetId))
            {
                _resetId = resetId;
                PasswordStep1.Visibility = Visibility.Collapsed;
                PasswordStep2.Visibility = Visibility.Visible;
                PasswordStepText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_Password_Step2_Hint");
                StatusText.Text = L("L_Settings_Security_Password_CodeSent");
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        private async void ConfirmResetCode_Click(object sender, RoutedEventArgs e)
        {
            var code = ResetCodeBox.Text?.Trim();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(_resetId))
            {
                StatusText.Text = L("L_Settings_Security_Password_EnterCode");
                return;
            }

            StatusText.Text = L("L_Settings_Security_Password_CheckingCode");
            var (error, refreshToken) = await App.ServerCommunication.ConfirmResetCode(
                _resetId, code, App.GParam);

            if (error.IsSuccess)
            {
                PasswordStep2.Visibility = Visibility.Collapsed;
                PasswordStep3.Visibility = Visibility.Visible;
                PasswordStepText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_Password_Step3_Hint");
                StatusText.Text = L("L_Settings_Security_Password_CodeConfirmed");
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        private async void SetNewPassword_Click(object sender, RoutedEventArgs e)
        {
            var newPass = NewPasswordBox.Password;
            var confirmPass = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(newPass))
            {
                StatusText.Text = L("L_Settings_Security_Password_EnterNew");
                return;
            }

            if (newPass != confirmPass)
            {
                StatusText.Text = L("L_Settings_Security_Password_Mismatch");
                return;
            }

            StatusText.Text = L("L_Common_Saving");
            var error = await App.ServerCommunication.SetPassword(newPass, App.GParam);
            if (error.IsSuccess)
            {
                StatusText.Text = L("L_Settings_Security_Password_Changed");
                NewPasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                // Вернуть на шаг 1
                PasswordStep3.Visibility = Visibility.Collapsed;
                PasswordStep1.Visibility = Visibility.Visible;
                PasswordStepText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_Password_Step1_Hint");
                _resetId = null;
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        #endregion

        #region 2FA

        private async void SetupTwoFa_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = L("L_Settings_Security_TwoFa_LoadingQr");
            var (error, qrBase64, justCode) = await App.ServerCommunication.OtpReceipt(App.GParam);
            if (!error.IsSuccess)
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
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
                    StatusText.Text = L("L_Settings_Security_TwoFa_QrError");
                    return;
                }
            }

            OtpCodeBox.Visibility = Visibility.Visible;
            ConfirmTwoFaButton.Visibility = Visibility.Visible;
            SetupTwoFaButton.Visibility = Visibility.Collapsed;
            StatusText.Text = L("L_Settings_Security_TwoFa_ScanHint");

            if (!string.IsNullOrEmpty(justCode))
            {
                var fmt = L("L_Settings_Security_TwoFa_ManualKey");
                TwoFaStatusText.Text = string.Format(fmt, justCode);
            }
        }

        private async void ConfirmTwoFa_Click(object sender, RoutedEventArgs e)
        {
            var code = OtpCodeBox.Text?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                StatusText.Text = L("L_Settings_Security_TwoFa_EnterOtp");
                return;
            }

            StatusText.Text = L("L_Settings_Security_TwoFa_Checking");
            var error = await App.ServerCommunication.OtpAccept(App.GParam, code);
            if (error.IsSuccess)
            {
                StatusText.Text = L("L_Settings_Security_TwoFa_Configured");
                QrImage.Visibility = Visibility.Collapsed;
                OtpCodeBox.Visibility = Visibility.Collapsed;
                ConfirmTwoFaButton.Visibility = Visibility.Collapsed;
                SetupTwoFaButton.Visibility = Visibility.Collapsed;
                DisableTwoFaButton.Visibility = Visibility.Visible;
                TwoFaStatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_TwoFa_Enabled");
            }
            else
            {
                StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
            }
        }

        private async void DisableTwoFa_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = L("L_Settings_Security_TwoFa_Disabling");
            DisableTwoFaButton.IsEnabled = false;
            try
            {
                var error = await App.ServerCommunication.OtpDisable(App.GParam);
                if (error.IsSuccess)
                {
                    StatusText.Text = L("L_Settings_Security_TwoFa_Disabled_Status");
                    DisableTwoFaButton.Visibility = Visibility.Collapsed;
                    SetupTwoFaButton.Visibility = Visibility.Visible;
                    TwoFaStatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "L_Settings_Security_TwoFa_Disabled");
                }
                else
                {
                    StatusText.Text = L("L_Common_Error_Prefix") + error.ErrorMessage;
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
                StatusText.Text = L("L_Settings_Security_Pin_Removed");
                return;
            }

            App.GParam.AppPass = pin;
            App.SaveGlobalParam();
            StatusText.Text = L("L_Settings_Security_Pin_Saved");
        }

        #endregion

        private static string L(string key)
            => Application.Current?.TryFindResource(key) as string ?? key;
    }
}
