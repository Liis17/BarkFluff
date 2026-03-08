using System.Diagnostics;
using System.Security.Principal;

namespace Barkfluff.Updater.CLI.Services
{
    /// <summary>
    /// Сервис для работы с правами администратора
    /// </summary>
    public class AdminService
    {
        /// <summary>
        /// Проверяет, запущен ли процесс с правами администратора
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Перезапускает приложение с правами администратора
        /// </summary>
        /// <param name="args">Аргументы командной строки для передачи в новый процесс</param>
        /// <returns>True если перезапуск успешен, False если пользователь отменил</returns>
        public static bool RestartAsAdmin(string[] args)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "runas", // Запрос прав администратора
                    Arguments = string.Join(" ", args)
                };

                Process.Start(processInfo);
                return true;
            }
            catch (Exception)
            {
                // Пользователь отменил запрос UAC или произошла ошибка
                return false;
            }
        }

        /// <summary>
        /// Получает путь к исполняемому файлу BarkFluff в указанной директории установки
        /// </summary>
        public static string GetBarkFluffExecutablePath(string installPath)
        {
            return System.IO.Path.Combine(installPath, "Barkfluff.exe");
        }
    }
}
