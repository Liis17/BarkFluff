using Barkfluff.Updater.CLI.Arguments;
using Barkfluff.Updater.CLI.Commands;
using Barkfluff.Updater.CLI.Services;
using Barkfluff.Updater.CLI.UI;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Barkfluff.Updater.CLI
{
    public class Program
    {
        static int Main(string[] args)
        {
            // Устанавливаем кодировку консоли для корректного отображения Unicode
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;

            // Включаем поддержку ANSI escape sequences для Windows
            EnableVirtualTerminalProcessing();

            // Парсим аргументы
            var parsedArgs = ArgumentParser.Parse(args);

            int exitCode = 0;

            // Обработка режима AutoUpdate (запуск без аргументов)
            if (parsedArgs.Mode == AppMode.AutoUpdate)
            {
                var downloadService = new DownloadService();
                if (downloadService.IsLocalInstallation())
                {
                    // Есть Barkfluff.exe рядом - запускаем обновление
                    parsedArgs.Mode = AppMode.Update;
                }
                else
                {
                    // Нет Barkfluff.exe - показываем справку
                    parsedArgs.Mode = AppMode.Help;
                }
            }

            switch (parsedArgs.Mode)
            {
                case AppMode.Install:
                    ConsoleUI.PrintWithGradient(LogoAssets.InstallLines);
                    exitCode = RunAsync(() => new InstallCommand().ExecuteAsync(parsedArgs.Silent));
                    break;

                case AppMode.Update:
                    ConsoleUI.PrintWithGradient(LogoAssets.UpdateLines);
                    exitCode = RunAsync(() => new UpdateCommand().ExecuteAsync(parsedArgs.Silent));
                    break;

                case AppMode.Help:
                default:
                    exitCode = new HelpCommand().Execute(parsedArgs.InvalidArguments);
                    break;
            }

            // Если не тихий режим - ждём нажатия клавиши
            if (!parsedArgs.Silent)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Завершение...");
                Thread.Sleep(1000);
                Console.ResetColor();
            }

            return exitCode;
        }

        private static int RunAsync(Func<Task<int>> taskFunc)
        {
            try
            {
                return taskFunc().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Критическая ошибка: {ex.Message}");
                return 1;
            }
        }

        private static void EnableVirtualTerminalProcessing()
        {
            try
            {
                // Для Windows 10+ включаем поддержку ANSI
                var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                if (handle != IntPtr.Zero)
                {
                    GetConsoleMode(handle, out uint mode);
                    SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
                }
            }
            catch
            {
                // Игнорируем ошибки на старых системах
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
