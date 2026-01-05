using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ServerItem.xaml
    /// </summary>
    public partial class ServerItem : UserControl
    {
        private readonly string _ip;

        public ServerItem(ServerDataElement serverData)
        {
            InitializeComponent();

            ServerTitle.Text = serverData.Title;
            ServerDescription.Text = serverData.Description;
            ServerUserCount.Text = FormatUserCount(serverData.UserCount);

            // Display public name as @servername if available
            if (!string.IsNullOrWhiteSpace(serverData.PublicName))
            {
                ServerPublicName.Text = $"@{serverData.PublicName}";
                ServerPublicName.Visibility = Visibility.Visible;
            }
            else
            {
                ServerPublicName.Visibility = Visibility.Collapsed;
            }

            _ip = serverData.Ip;
        }

        private static string FormatUserCount(string userCount)
        {
            if (long.TryParse(userCount, out var count))
            {
                return count switch
                {
                    >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
                    >= 1_000 => $"{count / 1_000.0:0.#}K",
                    _ => count.ToString()
                };
            }
            return userCount;
        }

        /// <summary>
        /// Sets the accent color for the server indicator
        /// </summary>
        public void SetAccentColor(string hexColor)
        {
            if (!string.IsNullOrWhiteSpace(hexColor))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hexColor);
                    ServerColorIndicator.Background = new SolidColorBrush(color);
                }
                catch
                {
                    // Keep default color if conversion fails
                }
            }
        }

        private async void PublicServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.GParam.SocketBeacon = _ip;
                var result = App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);

                if (!result.IsSuccess)
                {
                    App.ErideMessage.AddMessage(result.ErrorMessage ?? "Не удалось подключиться к серверу", new Erida { Type = MType.Error });
                    return;
                }

                var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);

                if (!error.IsSuccess)
                {
                    App.ErideMessage.AddMessage(error.ErrorMessage ?? "Не удалось получить информацию о сервере", new Erida { Type = MType.Error });
                    return;
                }

                App.ErideMessage.AddMessage("Получена информация о сервере", new Erida { Type = MType.Debug });

                App.GParam.ServerName = serverInfo.Name;
                App.GParam.ServerDescription = serverInfo.Description;
                App.GParam.SocketIdentity = WebApi.Core.WebApi.EnsureHttpPrefix($"{serverInfo.Identity.Endpoint.Host}:{serverInfo.Identity.Endpoint.Port}");
                App.GParam.SocketUsers = WebApi.Core.WebApi.EnsureHttpPrefix($"{serverInfo.Users.Endpoint.Host}:{serverInfo.Users.Endpoint.Port}");
                App.GParam.SocketFiles = WebApi.Core.WebApi.EnsureHttpPrefix($"{serverInfo.Files.Endpoint.Host}:{serverInfo.Files.Endpoint.Port}");
                App.GParam.SocketMessages = WebApi.Core.WebApi.EnsureHttpPrefix($"{serverInfo.Messages.Endpoint.Host}:{serverInfo.Messages.Endpoint.Port}");
                App.GParam.SocketUpdates = WebApi.Core.WebApi.EnsureHttpPrefix($"{serverInfo.Updates.Endpoint.Host}:{serverInfo.Updates.Endpoint.Port}");
                App.GParam.SocketOnliner = WebApi.Core.WebApi.EnsureHttpPrefix(serverInfo.Onliner.Endpoint.Host + ":" + serverInfo.Onliner.Endpoint.Port);
                App.GParam.Colors = new ClientColors
                {
                    LiteHex = serverInfo.Color.LiteHex,
                    MainHex = serverInfo.Color.MainHex,
                    HardHex = serverInfo.Color.HardHex,
                };
                MainWindow.SaveSettings();

                App.MessengerWindow.OpenLoginPage();
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
            {
                App.ErideMessage.AddMessage("Сервер недоступен", new Erida { Type = MType.Error });
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.DeadlineExceeded)
            {
                App.ErideMessage.AddMessage("Превышено время ожидания ответа от сервера", new Erida { Type = MType.Error });
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка подключения: {ex.Message}", new Erida { Type = MType.Error });
            }
        }
    }
}
