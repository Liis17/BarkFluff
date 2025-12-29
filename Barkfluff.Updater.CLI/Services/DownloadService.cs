using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Barkfluff.Updater.CLI.UI;

namespace Barkfluff.Updater.CLI.Services
{
    /// <summary>
    /// Сервис для скачивания и распаковки обновлений
    /// </summary>
    public class DownloadService
    {
        private const string AppFolderName = "BarkFluff";
        private const string MainExecutable = "Barkfluff.exe";

        /// <summary>
        /// Скачивает файл по URL и сохраняет в указанный путь
        /// </summary>
        public async Task<string> DownloadToTempAsync(string url, string fileName)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            try
            {
                ConsoleUI.PrintProgress($"Downloading: {fileName}");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BarkFluff-Updater");
                    
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        
                        // Инициализируем прогресс-бар
                        ConsoleUI.InitProgressBar();
                        
                        var stopwatch = Stopwatch.StartNew();
                        
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;
                            long lastSpeedCalcBytes = 0;
                            double lastSpeedCalcTime = 0;
                            double currentSpeed = 0;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                // Рассчитываем скорость каждые 200мс
                                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                if (elapsedSeconds - lastSpeedCalcTime >= 0.2)
                                {
                                    long bytesInInterval = totalRead - lastSpeedCalcBytes;
                                    double timeInterval = elapsedSeconds - lastSpeedCalcTime;
                                    currentSpeed = (bytesInInterval / timeInterval) / (1024 * 1024); // MB/s
                                    
                                    lastSpeedCalcBytes = totalRead;
                                    lastSpeedCalcTime = elapsedSeconds;
                                }

                                if (totalBytes > 0)
                                {
                                    int percent = (int)((totalRead * 100) / totalBytes);
                                    ConsoleUI.UpdateProgressBar(percent, totalRead, totalBytes, currentSpeed);
                                }
                            }
                        }
                        
                        ConsoleUI.FinishProgressBar();
                    }
                }

                ConsoleUI.PrintSuccess($"Downloaded: {tempPath}");
                return tempPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Download error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Распаковывает ZIP архив в указанную папку используя PowerShell
        /// </summary>
        public void ExtractZip(string zipPath, string destinationPath)
        {
            try
            {
                ConsoleUI.PrintProgress($"Extracting to: {destinationPath}...");

                // Создаем папку если не существует
                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                }

                // Используем PowerShell для распаковки (доступен на Windows 10+)
                var psCommand = $"Expand-Archive -Path '{zipPath}' -DestinationPath '{destinationPath}' -Force";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit();
                    
                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        throw new Exception($"PowerShell error code {process.ExitCode}: {error}");
                    }
                }

                ConsoleUI.PrintSuccess($"Extracted to: {destinationPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Extraction error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Удаляет временный файл
        /// </summary>
        public void CleanupTempFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    ConsoleUI.PrintProgress("Temporary files deleted");
                }
            }
            catch
            {
                // Игнорируем ошибки очистки
            }
        }

        /// <summary>
        /// Получает путь установки по умолчанию (AppData)
        /// </summary>
        public string GetDefaultInstallPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppFolderName);
        }

        /// <summary>
        /// Получает путь для обновления (проверяет наличие Barkfluff.exe рядом)
        /// </summary>
        public string GetUpdatePath()
        {
            // Получаем путь к текущему исполняемому файлу
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Path.Combine(currentDir, MainExecutable);

            // Если рядом есть Barkfluff.exe, обновляем в эту папку
            if (File.Exists(exePath))
            {
                return currentDir;
            }

            // Иначе используем путь по умолчанию
            return GetDefaultInstallPath();
        }

        /// <summary>
        /// Проверяет, есть ли Barkfluff.exe рядом с updater'ом
        /// </summary>
        public bool IsLocalInstallation()
        {
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Path.Combine(currentDir, MainExecutable);
            return File.Exists(exePath);
        }
    }
}
