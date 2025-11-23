using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

#pragma warning disable CA1416 
#pragma warning disable CS4014 

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class SelectServer : UserControl
    {
        public SelectServer()
        {
            InitializeComponent();
            Loaded += SelectServer_Loaded;
        }

        private async void SelectServer_Loaded(object sender, RoutedEventArgs e)
        {

            App.ServerCommunication.CreateNavigatorAC();
            var response = await App.ServerCommunication.GetServerList(App.GParam);
            if (!response.Item1.IsSuccess) { return; }
            NoServer.Visibility = Visibility.Collapsed;
            ServerList.Children.Clear();

            foreach (var item in response.ServerElements)
            {
                var server = new ServerItem(item);
                ServerList.Children.Add(server);
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverInput = ServerAddressTextBox.Text.Trim();

                if (string.IsNullOrEmpty(serverInput))
                {
                    App.ErideMessage.AddMessage("Укажите адрес сервера и порт", new Erida { Type = MType.Info });
                    return;
                }

                string pattern = @"^([^:]+):(\d+)$";
                var match = Regex.Match(serverInput, pattern);
                if (!match.Success)
                {
                    App.ErideMessage.AddMessage("Неверный формат. Используйте [домен или IP]:[порт]", new Erida { Type = MType.Info });
                    return;
                }

                string host = match.Groups[1].Value;
                string port = match.Groups[2].Value;

                string socket = host + ":" + port;
                App.GParam.SocketBeacon = socket;
                App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);

                try
                {
                    var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);
                    if (!error.IsSuccess)
                    {
                        App.ErideMessage.AddMessage(error.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                        return;
                    }
                    else
                    {
                        App.ErideMessage.AddMessage("Получена информация о сервере", new Erida { Type = MType.Debug });

                        App.GParam.ServerName = serverInfo.Name;
                        App.GParam.ServerDescription = serverInfo.Description;
                        App.GParam.SocketIdentity = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Identity.Endpoint.Host + ":" + serverInfo.Identity.Endpoint.Port);
                        App.GParam.SocketUsers = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Users.Endpoint.Host + ":" + serverInfo.Users.Endpoint.Port);
                        App.GParam.SocketFiles = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Files.Endpoint.Host + ":" + serverInfo.Files.Endpoint.Port);
                        App.GParam.SocketMessages = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Messages.Endpoint.Host + ":" + serverInfo.Messages.Endpoint.Port);
                        App.GParam.SocketUpdates = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Updates.Endpoint.Host + ":" + serverInfo.Updates.Endpoint.Port);
                        App.GParam.Colors = new ClientColors()
                        {
                            LiteHex = serverInfo.Color.LiteHex,
                            MainHex = serverInfo.Color.MainHex,
                            HardHex = serverInfo.Color.HardHex,
                        };
                        MainWindow.SaveSettings();
                    }
                }
                catch (Exception ex)
                {
                    App.ErideMessage.AddMessage($"Ошибка подключения: {ex.Message}", new Erida { Type = MType.Error });
                    return;
                }

                App.MessengerWindow.OpenLoginPage();


            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(ex.Message, new Erida { Type = MType.Error });
            }
        }

        private void ServerAddressTextBox_GotFocus(object sender, RoutedEventArgs e)
        {

        }

        private void ServerAddressTextBox_Loaded(object sender, RoutedEventArgs e)
        {
#if(DEBUG)
            if (sender is TextBox serverIp)
            {
                serverIp.Text = "charlie.liis17.ru:7002";
            }
#endif
        }

    }
}
