using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    public partial class SelectServer : UserControl
    {
        private const string SelfHostedUrl = "https://barkfluff.com/selfhosted";
        private const int MinPort = 1;
        private const int MaxPort = 65535;

        private static readonly Regex ServerAddressPattern = new(
            @"^([^:]+):(\d+)$",
            RegexOptions.Compiled);

        private bool _isConnecting;

        public SelectServer()
        {
            InitializeComponent();
            Loaded += SelectServer_Loaded;
        }

        private async void SelectServer_Loaded(object sender, RoutedEventArgs e)
        {
            App.ServerCommunication.CreateNavigatorAC();
            var response = await App.ServerCommunication.GetServerList(App.GParam);

            if (!response.Item1.IsSuccess)
            {
                return;
            }

            ServerList.Children.Clear();

            foreach (var item in response.ServerElements)
            {
                var server = new ServerItem(item);
                ServerList.Children.Add(server);
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            if (!ValidateServerAddress(ServerAddressTextBox.Text, out var host, out var port))
            {
                return;
            }

            SetConnectingState(true);

            try
            {
                var socket = $"{host}:{port}";
                App.GParam.SocketBeacon = socket;
                App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);

                var (error, serverInfo) = await App.ServerCommunication.GetServerInfo(App.GParam);

                if (!error.IsSuccess)
                {
                    ShowError(error.ErrorMessage ?? "Не удалось получить информацию о сервере");
                    return;
                }

                App.ErideMessage.AddMessage("Получена информация о сервере", new Erida { Type = MType.Debug });

                ApplyServerSettings(serverInfo);
                MainWindow.SaveSettings();

                App.MessengerWindow.OpenLoginPage();
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
            {
                ShowError("Сервер недоступен. Проверьте адрес и попробуйте снова");
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.DeadlineExceeded)
            {
                ShowError("Превышено время ожидания ответа от сервера");
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка подключения: {ex.Message}");
            }
            finally
            {
                SetConnectingState(false);
            }
        }

        private bool ValidateServerAddress(string input, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            var trimmedInput = input?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmedInput))
            {
                ShowError("Укажите адрес сервера");
                return false;
            }

            var match = ServerAddressPattern.Match(trimmedInput);
            if (!match.Success)
            {
                ShowError("Используйте формат: адрес:порт");
                return false;
            }

            host = match.Groups[1].Value.Trim();

            if (!int.TryParse(match.Groups[2].Value, out port) || port < MinPort || port > MaxPort)
            {
                ShowError($"Порт должен быть числом от {MinPort} до {MaxPort}");
                return false;
            }

            if (!IsValidHost(host))
            {
                ShowError("Некорректный адрес сервера");
                return false;
            }

            return true;
        }

        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            // Check if it's a valid IP address
            if (IPAddress.TryParse(host, out _))
            {
                return true;
            }

            // Check if it's a valid DNS hostname using Uri.CheckHostName
            var hostType = Uri.CheckHostName(host);
            return hostType == UriHostNameType.Dns || hostType == UriHostNameType.Basic;
        }

        private void SetConnectingState(bool isConnecting)
        {
            _isConnecting = isConnecting;
            ConnectButton.IsEnabled = !isConnecting;
        }

        private static void ShowError(string message)
        {
            App.ErideMessage.AddMessage(message, new Erida { Type = MType.Error });
        }

        private static void ApplyServerSettings(BarkFluff.Proto.Beacon.GetServerInfoResponse serverInfo)
        {
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
        }

        private void TextBlock_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelfHostedUrl,
                    UseShellExecute = true
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Expected when no default browser is configured or the URL handler fails.
                // This is a non-critical failure - the user can manually navigate to the URL.
            }
            catch (InvalidOperationException)
            {
                // Expected when the process cannot be started (e.g., file not found).
                // This is a non-critical failure - the user can manually navigate to the URL.
            }
        }
    }
}
