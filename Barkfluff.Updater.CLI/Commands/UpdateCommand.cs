using System;
using System.Threading.Tasks;
using Barkfluff.Updater.CLI.Arguments;
using Barkfluff.Updater.CLI.UI;

namespace Barkfluff.Updater.CLI.Commands
{
    /// <summary>
    /// Команда обновления приложения
    /// </summary>
    public class UpdateCommand
    {
        private readonly Services.GitHubReleaseService _releaseService;
        private readonly Services.DownloadService _downloadService;
        private readonly Services.ProtocolRegistrationService _protocolService;
        private readonly Services.ShortcutService _shortcutService;

        public UpdateCommand()
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
                ConsoleUI.PrintHeader("Updating BarkFluff");
                Console.WriteLine();

                // 1. Определяем путь обновления
                string updatePath;
                if (_downloadService.IsLocalInstallation())
                {
                    updatePath = _downloadService.GetUpdatePath();
                    ConsoleUI.PrintInfo("Local installation detected");
                    ConsoleUI.PrintProgress($"Update path: {updatePath}");
                }
                else
                {
                    updatePath = _downloadService.GetDefaultInstallPath();
                    ConsoleUI.PrintInfo("No local installation found");
                    ConsoleUI.PrintProgress($"Update path: {updatePath}");
                }

                // 2. Получаем последний стабильный релиз
                ConsoleUI.PrintInfo("Checking releases repository...");
                var release = await _releaseService.GetLatestStableReleaseAsync();

                if (release == null)
                {
                    ConsoleUI.PrintError("No stable release found (Master/Release)");
                    return 1;
                }

                ConsoleUI.PrintSuccess($"Found release: {release.TagName}");
                ConsoleUI.PrintProgress($"Channel: {release.Channel}, Version: {release.Version}");

                // 3. Скачиваем архив
                ConsoleUI.PrintInfo("Downloading update...");
                var zipPath = await _downloadService.DownloadToTempAsync(release.DownloadUrl, release.FileName);

                // 4. Распаковываем
                ConsoleUI.PrintInfo("Applying update...");
                _downloadService.ExtractZip(zipPath, updatePath);

                // 5. Очистка временных файлов
                _downloadService.CleanupTempFile(zipPath);

                // 6. Обновление регистрации протокола и ярлыка (требует прав администратора)
                var exePath = Services.AdminService.GetBarkFluffExecutablePath(updatePath);
                
                try
                {
                    Console.WriteLine();
                    ConsoleUI.PrintInfo("Updating system integration...");
                    
                    _protocolService.RegisterProtocol(exePath);
                    _shortcutService.CreateStartMenuShortcut(exePath);
                }
                catch (UnauthorizedAccessException)
                {
                    ConsoleUI.PrintWarning("System integration update skipped (Administrator rights required)");
                    ConsoleUI.PrintProgress("Protocol and Start Menu shortcut were not updated");
                }

                Console.WriteLine();
                ConsoleUI.PrintSuccess("Update completed successfully!");
                ConsoleUI.PrintInfo($"BarkFluff updated in: {updatePath}");

                return 0;
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Update error: {ex.Message}");
                return 1;
            }
        }
    }
}
