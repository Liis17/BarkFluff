using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;
namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ServerItem.xaml
    /// </summary>
    public partial class ServerItem : UserControl
    {
        private string _ip = string.Empty;
        public ServerItem(ServerDataElement serverData)
        {
            InitializeComponent();
            ServerTitle.Text = serverData.Title;
            ServerDescription.Text = serverData.Description;
            ServerInfo.Text = $@"{serverData.Ip} • {serverData.UserCount}";
            _ip = serverData.Ip;
        }

        private async void PublicServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.GParam.SocketBeacon = _ip;
                var a = App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);
                if (!a.IsSuccess)
                {
                    App.ErideMessage.AddMessage(a.ErrorMessage ?? "Неизвестная проблема", new Erida { Type = MType.Error });
                    return;
                }

                try
                {
                    var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);
                    if (!error.IsSuccess)
                    {
                        App.ErideMessage.AddMessage(error.ErrorMessage, new Erida { Type = MType.Error });
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
                    App.ErideMessage.AddMessage($"Ошибка получения информации о сервере: {ex.Message}", new Erida { Type = MType.Error });
                    return;
                }
                App.MessengerWindow.OpenLoginPage();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Неизвестная ошибка: {ex.Message}", new Erida { Type = MType.Error });
            }
        }
    }
}
