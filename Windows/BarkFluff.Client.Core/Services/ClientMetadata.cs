using Microsoft.Win32;

namespace BarkFluff.Client.Core.Services;

public static class ClientMetadata
{
    public const string AppName = "BarkFluff";
    public const string AppVersion = "2.0";

    public static string OperatingSystem
    {
        get
        {
            var buildNumber = Environment.OSVersion.Version.Build;
            var windowsName = buildNumber >= 22000
                ? "Windows 11"
                : buildNumber >= 10240
                    ? "Windows 10"
                    : $"Windows (сборка {buildNumber})";

            if (buildNumber < 10240)
            {
                return windowsName;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var edition = key?.GetValue("EditionID")?.ToString();
                var displayVersion = key?.GetValue("DisplayVersion")?.ToString()
                    ?? key?.GetValue("ReleaseId")?.ToString();

                if (!string.IsNullOrEmpty(edition) && !string.IsNullOrEmpty(displayVersion))
                {
                    return $"{windowsName} {edition} {displayVersion} (сборка {buildNumber})";
                }
            }
            catch
            {
                // Если реестр недоступен, возвращаем версию Windows без редакции.
            }

            return $"{windowsName} (сборка {buildNumber})";
        }
    }
}
