using BarkFluff.WebApi.Core.MessengerData;

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    /// <summary>
    /// Логика взаимодействия для SelectServer.xaml
    /// </summary>
    public partial class SelectServer : UserControl
    {
        public SelectServer()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverInput = ServerAddressTextBox.Text.Trim();

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

                string socket = host + ":" + port;
                App.GParam.SocketBeacon = socket;
                App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);

                try
                {
                    App.ServerCommunication.GetServerInfo(App.GParam);
                    GlobalParam.Save(App.GParam, Path.Combine(App.GParam.AppPath, "GlobalParam.json"), App.GParam.AppPass);
                }
                catch (Exception ex)
                {
                    ErrorText.Text = $"Ошибка подключения: {ex.Message}";
                    return;
                }

                App.MessengerWindow.OpenLoginPage();


            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Неизвестная ошибка: {ex.Message}";
            }
        }

        private void ServerAddressTextBox_GotFocus(object sender, RoutedEventArgs e)
        {

        }

        private void PublicServer_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
