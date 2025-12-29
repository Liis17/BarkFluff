using System;
using System.Threading.Tasks;
using Barkfluff.Updater.CLI.Arguments;
using Barkfluff.Updater.CLI.UI;

namespace Barkfluff.Updater.CLI.Commands
{
    /// <summary>
    /// Команда установки приложения
    /// </summary>
    public class InstallCommand
    {
        private readonly Services.GitHubReleaseService _releaseService;
        private readonly Services.DownloadService _downloadService;
        private readonly Services.ProtocolRegistrationService _protocolService;
        private readonly Services.ShortcutService _shortcutService;

        public InstallCommand()
        {
            _releaseService = new Services.GitHubReleaseService();
            _downloadService = new Services.DownloadService();
            _protocolService = new Services.ProtocolRegistrationService();
            _shortcutService = new Services.ShortcutService();
        }

        public async Task<int> ExecuteAsync(bool silent)
        {
            try
            {
                ConsoleUI.PrintHeader("Installing BarkFluff");
                Console.WriteLine();

                // 1. Получаем последний стабильный релиз
                ConsoleUI.PrintInfo("Checking releases repository...");
                var release = await _releaseService.GetLatestStableReleaseAsync();

                if (release == null)
                {
                    ConsoleUI.PrintError("No stable release found (Master/Release)");
                    return 1;
                }

                ConsoleUI.PrintSuccess($"Found release: {release.TagName}");
                ConsoleUI.PrintProgress($"Channel: {release.Channel}, Version: {release.Version}");

                // 2. Скачиваем архив
                ConsoleUI.PrintInfo("Downloading update...");
                var zipPath = await _downloadService.DownloadToTempAsync(release.DownloadUrl, release.FileName);

                // 3. Распаковываем в AppData
                var installPath = _downloadService.GetDefaultInstallPath();
                ConsoleUI.PrintInfo($"Installing to: {installPath}");
                _downloadService.ExtractZip(zipPath, installPath);

                // 4. Очистка временных файлов
                _downloadService.CleanupTempFile(zipPath);

                // 5. Регистрация протокола и создание ярлыка (требует прав администратора)
                var exePath = Services.AdminService.GetBarkFluffExecutablePath(installPath);
                
                try
                {
                    Console.WriteLine();
                    ConsoleUI.PrintInfo("Configuring system integration...");
                    
                    _protocolService.RegisterProtocol(exePath);
                    _shortcutService.CreateStartMenuShortcut(exePath);
                }
                catch (UnauthorizedAccessException)
                {
                    ConsoleUI.PrintWarning("System integration skipped (Administrator rights required)");
                    ConsoleUI.PrintProgress("Protocol and Start Menu shortcut were not created");
                }

                Console.WriteLine();
                ConsoleUI.PrintSuccess("Installation completed successfully!");
                ConsoleUI.PrintInfo($"BarkFluff installed to: {installPath}");

                return 0;
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Installation error: {ex.Message}");
                return 1;
            }
        }
    }
}
