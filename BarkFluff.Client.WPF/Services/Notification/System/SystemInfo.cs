using Microsoft.Win32;

using System.Net.Http;

namespace BarkFluff.Client.WPF
{
    public class SystemInfo
    {
        private static string GetDisplayVersion()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key != null)
                {
                    object editionID = key.GetValue("EditionID");

                    object displayVersion = key.GetValue("DisplayVersion");
                    if (displayVersion != null && editionID != null)
                    {
                        return $"{editionID.ToString()} {displayVersion.ToString()}";
                    }
                    object releaseId = key.GetValue("ReleaseId");
                    if (releaseId != null && editionID != null)
                    {
                        return $"{editionID.ToString()} {releaseId.ToString()}";
                    }
                }
                return string.Empty;
            }
        }

        public static string GetFriendlyWindowsVersion()
        {
            OperatingSystem os = Environment.OSVersion;
            Version version = os.Version;
            int buildNumber = version.Build;
            string windowsName = "Windows ";

            if (buildNumber >= 22000)
            {
                windowsName += "11";
            }
            else if (buildNumber >= 10240)
            {
                windowsName += "10";
            }
            else
            {
                windowsName += $"(сборка {buildNumber})";
                return windowsName;
            }

            try
            {
                string displayVersion = GetDisplayVersion();
                if (!string.IsNullOrEmpty(displayVersion))
                {
                    return $"{windowsName} {displayVersion} (сборка {buildNumber})";
                }
            }
            catch
            {
                // Если не удалось получить через реестр, продолжаем с обычным форматом
            }

            return $"{windowsName} (сборка {buildNumber})";
        }

        public static async Task<string> GetExternalIp()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    string ip = await client.GetStringAsync("https://ifconfig.me");
                    return ip.Trim();
                }
            }
            catch
            {
                return string.Empty;
            }
        }


        public static string GetAppPath()
        {
            string exePath = AppContext.BaseDirectory;
            return System.IO.Path.GetDirectoryName(exePath);
        }
    }
}
