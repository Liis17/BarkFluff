using Barkfluff.Updater.CLI.UI;

using System.Diagnostics;

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

                // 2. Проверяем последний стабильный релиз
                ConsoleUI.PrintInfo("Checking releases repository...");
                var release = await _releaseService.GetLatestStableReleaseAsync();

                if (release == null)
                {
                    ConsoleUI.PrintError("No stable release found (Master/Release)");
                    return 1;
                }

                ConsoleUI.PrintSuccess($"Found release: {release.TagName}");
                ConsoleUI.PrintProgress($"Channel: {release.Channel}, Version: {release.Version}");

                // 3. Вызываем протокол bf://closetoupdate для закрытия приложения
                ConsoleUI.PrintInfo("Requesting application to close...");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "bf://closetoupdate",
                        UseShellExecute = true
                    });

                    ConsoleUI.PrintProgress("Waiting for application to close...");
                    await Task.Delay(3000); // Ждем 3 секунды
                }
                catch
                {
                    ConsoleUI.PrintWarning("Could not send close request (application may not be running)");
                }

                // 4. Скачиваем архив
                ConsoleUI.PrintInfo("Downloading update...");
                var zipPath = await _downloadService.DownloadToTempAsync(release.DownloadUrl, release.FileName);

                // 5. Распаковываем
                ConsoleUI.PrintInfo("Applying update...");
                _downloadService.ExtractZip(zipPath, updatePath);

                // 6. Удаляем временный архив
                _downloadService.CleanupTempFile(zipPath);

                // 7. Обновляем регистрацию протокола и ярлыка (только если администратор)
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

                // 8. Запускаем обновленное приложение
                if (!silent)
                {
                    ConsoleUI.PrintInfo("Launching BarkFluff...");
                }

                try
                {
                    if (System.IO.File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = "--successfulupdate",
                            UseShellExecute = true,
                            WorkingDirectory = updatePath
                        });

                        if (!silent)
                        {
                            ConsoleUI.PrintSuccess("BarkFluff launched successfully");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleUI.PrintWarning($"Could not launch application: {ex.Message}");
                }

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
