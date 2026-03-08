using BarkFluff.Client.WPF.Services.App.Converter;

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarkFluff.Client.WPF.Pages.SetupPages.Registration
{
    /// <summary>
    /// Логика взаимодействия для TwoFA.xaml
    /// </summary>
    public partial class TwoFA : UserControl
    {
        public CreateAccount? Pattern;
        private TextBox[]? codeBoxes;
        private bool isCodeSent = false; // Флаг для проверки, был ли код отправлен
        private Grid? initialPromptPanel;
        private Grid? qrCodePanel;
        private bool twoFAEnabled = false;

        public TwoFA()
        {
            InitializeComponent();
            Loaded += TwoFA_Loaded;
        }

        private void TwoFA_Loaded(object sender, RoutedEventArgs e)
        {
            // Save references to the panels
            initialPromptPanel = InitialPromptPanel;
            qrCodePanel = QrCodePanel;

            // Initialize code boxes array
            codeBoxes = new[] { CodeBox0, CodeBox1, CodeBox2, CodeBox3, CodeBox4, CodeBox5 };

            // Set initial state - show prompt panel, hide QR code panel
            if (initialPromptPanel != null)
            {
                initialPromptPanel.Visibility = Visibility.Visible;
            }
            if (qrCodePanel != null)
            {
                qrCodePanel.Visibility = Visibility.Collapsed;
            }
        }


        public async void Update()
        {
            if (!twoFAEnabled)
            {
                return;
            }

            var response = await App.ServerCommunication.OtpReceipt(App.GParam);
            var qr = Base64ToBitmapSource.ConvertBase64ToBitmapSource(response.qrBase64);
            QrCodeImage.Source = qr;
            SecretKeyText.Text = response.justCode;
        }

        private void EnableTwoFAButton_Click(object sender, RoutedEventArgs e)
        {
            // Show QR code panel and hide initial prompt panel
            if (initialPromptPanel != null)
            {
                initialPromptPanel.Visibility = Visibility.Collapsed;
            }
            if (qrCodePanel != null)
            {
                qrCodePanel.Visibility = Visibility.Visible;
            }

            twoFAEnabled = true;
            Update(); // Load QR code

            // Focus on the first code box
            if (CodeBox0 != null)
            {
                CodeBox0.Focus();
            }
        }

        private void DisableTwoFAButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip 2FA setup and proceed to next step
            Pattern?.NextStep();
        }
        private void CodeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }

        private void CodeBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox current = sender as TextBox;
            if (current.Text.Length == 1)
                current.Select(1, 0); // Чтобы курсор не прыгал
            else
                current.SelectAll();
        }

        private async void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
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
                    ConnectionText.Text = "Подключение...";
                    isCodeSent = true; // Флаг, что код отправлен
                    var response = await App.ServerCommunication.OtpAccept(App.GParam, code); // Отправка кода на сервер
                    if (response.IsSuccess)
                    {
                        Pattern.NextStep();
                    }
                    else
                    {
                        isCodeSent = false; // Сбрасываем флаг, если ответ не успешен
                        App.ErideMessage.AddMessage($"Ошибка: {response.ErrorMessage}", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                        ConnectionText.Text = "Подключиться";
                    }

                }
                catch (BarkFluff.Shared.Exceptions.Identity.NotValidOtpCodeException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код неверный
                    App.ErideMessage.AddMessage("Ошибка: Неверный код 2FA.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    return;
                }
                catch (BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код не был создан
                    App.ErideMessage.AddMessage("Ошибка: Код не был создан.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    return;
                }
                catch (BarkFluff.Shared.Exceptions.Identity.OtpCodeNeedException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код не нужен
                    App.ErideMessage.AddMessage("Ошибка: Обязательно необходимо ввести код 2FA.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    return;
                }
            }
        }

        private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
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
        private void CopySecretButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(SecretKeyText.Text); //Копировать в буфер
            });
        }
        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            Pattern.NextStep(); //пропустить подключение
        }
        private void FocusFirstCodeBox(object sender, RoutedEventArgs e)
        {
            //Pattern.NextStep();
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            App.ErideMessage.AddMessage("Ошибка: Обязательно необходимо ввести код 2FA.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
        }
    }
}
