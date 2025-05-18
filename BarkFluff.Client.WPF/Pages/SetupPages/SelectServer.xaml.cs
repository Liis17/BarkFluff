using BarkFluff.WebApi.Core.MessengerData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

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
        private async void Button_Click(object sender, RoutedEventArgs e)
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
                    GlobalParam.Save(App.GParam, Path.Combine( App.GParam.AppPath, "GlobalParam.json"), App.GParam.AppPass);
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
    }
}
