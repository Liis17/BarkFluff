using BarkFluff.Client.WPF.Services.App.Converter;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        public TwoFA()
        {
            InitializeComponent();
            Loaded += TwoFA_Loaded;
        }

        private void TwoFA_Loaded(object sender, RoutedEventArgs e)
        {
            codeBoxes = new[] { CodeBox0, CodeBox1, CodeBox2, CodeBox3, CodeBox4, CodeBox5 };
            CodeBox0.Focus();
        }


        public async void Update()
        {
            var response = await App.ServerCommunication.OtpReceipt(App.GParam);
            var qr = Base64ToBitmapSource.ConvertBase64ToBitmapSource(response.qrBase64);
            QrCodeImage.Source = qr;
            SecretKeyText.Text = response.justCode;
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
                    await App.ServerCommunication.OtpAccept(App.GParam,code); // Отправка кода на сервер
                    Pattern.NextStep();
                }
                catch(BarkFluff.Shared.Exceptions.Identity.NotValidOtpCodeException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код неверный
                    MessageBox.Show("Ошибка: Неверный код 2FA.");
                    return;
                }
                catch (BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код не был создан
                    MessageBox.Show("Ошибка: Код не был создан.");
                    return;
                }
                catch (BarkFluff.Shared.Exceptions.Identity.OtpCodeNeedException)
                {
                    isCodeSent = false; // Сбрасываем флаг, если код не нужен
                    MessageBox.Show("Ошибка: Обязательно необходимо ввести код 2FA.");
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
            Pattern.NextStep();
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Ошибка: Обязательно необходимо ввести код 2FA.");
        }
    }
}
