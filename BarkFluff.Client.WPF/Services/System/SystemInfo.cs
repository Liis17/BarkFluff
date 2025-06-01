using Microsoft.Win32;
using System.Diagnostics;

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

        public static string GetExternalIp()
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl",
                Arguments = "ifconfig.me",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException("Не удалось запустить процесс curl.");
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Trim();
            }
        }
    }
}
