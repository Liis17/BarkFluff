using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

namespace BarkFluff.Client.WPF.Pages
{
    /// <summary>
    /// Логика взаимодействия для Login.xaml
    /// </summary>
    public partial class Login : Page
    {
        public Login()
        {
            InitializeComponent();
            ErrorText.Text = "";
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverInput = ServerAddress.Text.Trim();

                if (string.IsNullOrEmpty(serverInput))
                {
                    ErrorText.Text = "Укажите адрес сервера и порт";
                    return;
                }

                string pattern = @"^([^:]+):(\d+)$";
                var match = Regex.Match(serverInput, pattern);
                if (!match.Success)
                {
                    ErrorText.Text = "Неверный формат. Используйте [домен или IP]:[порт]";
                    return;
                }

                string host = match.Groups[1].Value;
                string port = match.Groups[2].Value;

                // Создаем HttpClient с обработкой самоподписанных сертификатов
                var handler = new HttpClientHandler
                {
                    // Временно игнорируем ошибки сертификата (ТОЛЬКО ДЛЯ ТЕСТОВ!)
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    // Используем HTTPS вместо HTTP
                    string url = $"https://{host}:{port}/testping";

                    try
                    {
                        HttpResponseMessage response = await client.GetAsync(url);
                        response.EnsureSuccessStatusCode();

                        string responseBody = await response.Content.ReadAsStringAsync();
                        NextStep();
                    }
                    catch (HttpRequestException ex)
                    {
                        ErrorText.Text = $"Ошибка HTTP: {ex.Message}";
                        if (ex.InnerException != null)
                        {
                            ErrorText.Text = $"Дополнительно: {ex.InnerException.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Неизвестная ошибка: {ex.Message}";
            }
        }
        private void NextStep()
        {
            // Ваш код для следующего шага
            ErrorText.Text = "Подключение успешно, выполняем следующий шаг";
        }
    }
}
