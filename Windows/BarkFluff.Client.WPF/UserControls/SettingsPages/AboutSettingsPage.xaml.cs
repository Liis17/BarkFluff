using BarkFluff.Proto.Beacon;

using Microsoft.Win32;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class AboutSettingsPage : BaseSettingsPage
    {
        public override string Title => "О приложении";

        public AboutSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadAppInfo();
            LoadSystemInfo();
            LoadServerAddress();
        }

        // ──────────────────────────────────────────────────────────────
        // Информация о приложении
        // ──────────────────────────────────────────────────────────────
        private void LoadAppInfo()
        {
            // AppVersion.Version — единственный источник правды (обновляется IncrementVersion при запуске)
            AppVersionText.Text = $"Версия: {AppVersion.Version} {AppVersion.VersionName} ({AppVersion.VersionType})";

            // RuntimeInformation.FrameworkDescription уже содержит ".NET X.X.X" — не дублируем префикс
            var fw = RuntimeInformation.FrameworkDescription; // ".NET 10.0.5"
            DotnetVersionText.Text = fw.StartsWith(".NET ", StringComparison.OrdinalIgnoreCase)
                ? fw                          // уже читаемо: ".NET 10.0.5"
                : $".NET: {fw}";
        }

        // ──────────────────────────────────────────────────────────────
        // Информация о системе
        // ──────────────────────────────────────────────────────────────
        private void LoadSystemInfo()
        {
            OsVersionText.Text = GetWindowsFriendlyName();
            CpuText.Text = GetCpuName();
            RamText.Text = GetRamInfo();
            ArchText.Text = RuntimeInformation.OSArchitecture.ToString();
        }

        private static string GetWindowsFriendlyName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var product = key.GetValue("ProductName") as string ?? string.Empty;
                    var displayVersion = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    var build = key.GetValue("CurrentBuildNumber") as string ?? string.Empty;
                    return $"{product} {displayVersion} (сборка {build})".Trim();
                }
            }
            catch { }
            return RuntimeInformation.OSDescription;
        }

        private static string GetCpuName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    var name = key.GetValue("ProcessorNameString") as string;
                    if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                }
            }
            catch { }
            return "Неизвестно";
        }

        private static string GetRamInfo()
        {
            // GetTotalMemory — только управляемая куча, зато без зависимостей.
            // Для общего объёма RAM используем нативный вызов GlobalMemoryStatusEx.
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                {
                    double gb = status.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    return $"{gb:F1} ГБ";
                }
            }
            catch { }
            return "Неизвестно";
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        // ──────────────────────────────────────────────────────────────
        // Адрес сервера
        // ──────────────────────────────────────────────────────────────
        private void LoadServerAddress()
        {
            var beacon = App.GParam?.SocketBeacon;
            ServerAddressText.Text = string.IsNullOrEmpty(beacon) ? "Нет подключения" : beacon;
        }

        // ──────────────────────────────────────────────────────────────
        // Проверка соединения — запрос к Beacon
        // ──────────────────────────────────────────────────────────────
        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            PingButton.IsEnabled = false;
            PingStatusText.Text = "Подключение...";
            PingStatusText.Foreground = (Brush)FindResource("DescriptionText");
            PingStatusText.Visibility = Visibility.Visible;
            ServicesList.Visibility = Visibility.Collapsed;
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
                    ServicesList.Visibility = Visibility.Visible;
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

    // ──────────────────────────────────────────────────────────────────
    // ViewModel строки микросервиса
    // ──────────────────────────────────────────────────────────────────
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
