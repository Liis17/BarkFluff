using BarkFluff.Proto.Beacon;

using System.Windows;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class AboutServerSettingsPage : BaseSettingsPage
    {
        public override string Title => "О сервере";

        public AboutServerSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadServerInfo();
            // Автоматический пинг при открытии — как в macOS-эталоне
            _ = AutoPingAsync();
        }

        private void LoadServerInfo()
        {
            var gp = App.GParam;
            ServerNameText.Text = string.IsNullOrEmpty(gp?.ServerName) ? "Нет подключения" : gp.ServerName;

            var beacon = gp?.SocketBeacon;
            ServerAddressText.Text = string.IsNullOrEmpty(beacon) ? "—" : beacon;

            if (!string.IsNullOrEmpty(gp?.ServerDescription))
            {
                ServerDescriptionText.Text = gp.ServerDescription;
                ServerDescriptionText.Visibility = Visibility.Visible;
            }
            else
            {
                ServerDescriptionText.Visibility = Visibility.Collapsed;
            }
        }

        private async Task AutoPingAsync()
        {
            await DoPingAsync();
        }

        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            await DoPingAsync();
        }

        private async Task DoPingAsync()
        {
            PingButton.IsEnabled = false;
            PingStatusText.Text = "Подключение...";
            PingStatusText.Foreground = (Brush)FindResource("DescriptionText");
            PingStatusText.Visibility = Visibility.Visible;
            ServicesList.ItemsSource = null;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (error, info) = await App.ServerCommunication.GetServerInfo(App.GParam);
                sw.Stop();

                if (!error.IsSuccess || info == null)
                {
                    PingStatusText.Text = $"Ошибка: {error.ErrorMessage}";
                    PingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                }
                else
                {
                    PingStatusText.Text = $"Соединение установлено · {sw.ElapsedMilliseconds} мс  ·  {info.Name}";
                    PingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x57, 0xBB, 0x62));

                    ServicesList.ItemsSource = BuildServiceItems(info);
                }
            }
            catch (Exception ex)
            {
                PingStatusText.Text = $"Исключение: {ex.Message}";
                PingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            }
            finally
            {
                PingButton.IsEnabled = true;
            }
        }

        private static List<ServiceItem> BuildServiceItems(GetServerInfoResponse info)
        {
            (string Name, Service? Svc)[] services =
            [
                ("Identity", info.Identity),
                ("Users",    info.Users),
                ("Files",    info.Files),
                ("Messages", info.Messages),
                ("Updates",  info.Updates),
                ("Onliner",  info.Onliner),
                ("FastAuth", info.FastAuth)
            ];
            return [.. services.Select(s => new ServiceItem(s.Name, s.Svc))];
        }
    }

    internal sealed class ServiceItem
    {
        public string Name { get; }
        public string Endpoint { get; }
        public string StatusLabel { get; }
        public SolidColorBrush StatusColor { get; }

        public ServiceItem(string name, Service? service)
        {
            Name = name;

            if (service == null)
            {
                Endpoint = string.Empty;
                StatusLabel = "нет данных";
                StatusColor = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6));
                return;
            }

            Endpoint = service.Endpoint != null
                ? $"{service.Endpoint.Host}:{service.Endpoint.Port}"
                : string.Empty;

            (StatusLabel, StatusColor) = service.Status switch
            {
                ServiceStatus.Healthy => ("Работает", new SolidColorBrush(Color.FromRgb(0x57, 0xBB, 0x62))),
                ServiceStatus.Degraded => ("Деградация", new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38))),
                ServiceStatus.Unhealthy => ("Недоступен", new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))),
                ServiceStatus.Offline => ("Офлайн", new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))),
                _ => ("Неизвестно", new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6))),
            };
        }
    }
}
