using Microsoft.Win32;

using System.Runtime.InteropServices;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    public partial class AboutAppSettingsPage : BaseSettingsPage
    {
        public override string Title => "О приложении";

        public AboutAppSettingsPage()
        {
            InitializeComponent();
        }

        public override void OnNavigatedTo()
        {
            LoadAppInfo();
            LoadSystemInfo();
        }

        private void LoadAppInfo()
        {
            AppVersionText.Text = $"Версия: {AppVersion.Version} {AppVersion.VersionName} ({AppVersion.VersionType})";

            var fw = RuntimeInformation.FrameworkDescription;
            DotnetVersionText.Text = fw.StartsWith(".NET ", StringComparison.OrdinalIgnoreCase)
                ? fw
                : $".NET: {fw}";
        }

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
    }
}
