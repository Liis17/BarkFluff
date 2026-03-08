namespace Barkfluff.Updater.CLI.UI
{
    /// <summary>
    /// Класс для вывода в консоль с поддержкой градиента и форматирования
    /// </summary>
    public static class ConsoleUI
    {
        // Цвета градиента: #ff4141 -> #d69d85
        private static readonly int StartR = 0xff, StartG = 0x41, StartB = 0x41;
        private static readonly int EndR = 0xd6, EndG = 0x9d, EndB = 0x85;

        private static int _progressBarTop = -1;
        private static readonly object _lock = new object();

        public static void PrintWithGradient(string[] lines, int delayMs = 40)
        {
            int totalRows = lines.Length;
            int maxCols = 0;
            foreach (var line in lines)
            {
                if (line.Length > maxCols)
                    maxCols = line.Length;
            }

            for (int row = 0; row < lines.Length; row++)
            {
                string line = lines[row];
                for (int col = 0; col < line.Length; col++)
                {
                    double t = (double)(row + col) / (totalRows + maxCols - 2);

                    int r = (int)(StartR + (EndR - StartR) * t);
                    int g = (int)(StartG + (EndG - StartG) * t);
                    int b = (int)(StartB + (EndB - StartB) * t);

                    Console.Write($"\x1b[38;2;{r};{g};{b}m{line[col]}");
                }
                Console.WriteLine("\x1b[0m");
                if (delayMs > 0)
                    Thread.Sleep(delayMs);
            }
        }

        public static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [+] {message}");
            Console.ResetColor();
        }

        public static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [X] {message}");
            Console.ResetColor();
        }

        public static void PrintWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [!] {message}");
            Console.ResetColor();
        }

        public static void PrintInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [i] {message}");
            Console.ResetColor();
        }

        public static void PrintProgress(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      {message}");
            Console.ResetColor();
        }

        public static void PrintHeader(string header)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  {header}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {new string('-', header.Length)}");
            Console.ResetColor();
        }

        public static void PrintHelp(ArgumentInfo[] arguments)
        {
            Console.WriteLine();
            PrintHeader("Available arguments:");
            Console.WriteLine();

            int maxArgLength = 0;
            foreach (var arg in arguments)
            {
                string argsStr = string.Join(", ", arg.Names);
                if (argsStr.Length > maxArgLength)
                    maxArgLength = argsStr.Length;
            }

            foreach (var arg in arguments)
            {
                string argsStr = string.Join(", ", arg.Names);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"    {argsStr.PadRight(maxArgLength + 2)}");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(arg.Description);
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        public static void PrintInvalidArguments(string[] invalidArgs)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [!] Unknown arguments:");
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var arg in invalidArgs)
            {
                Console.WriteLine($"      {arg}");
            }
            Console.ResetColor();
        }

        /// <summary>
        /// Инициализирует прогресс-бар загрузки
        /// </summary>
        public static void InitProgressBar()
        {
            lock (_lock)
            {
                Console.WriteLine();
                Console.WriteLine();
                _progressBarTop = Console.CursorTop - 1;
            }
        }

        /// <summary>
        /// Обновляет прогресс-бар скачивания
        /// </summary>
        public static void UpdateProgressBar(int percent, long bytesDownloaded, long totalBytes, double speedMBps)
        {
            lock (_lock)
            {
                if (_progressBarTop < 0) return;

                int barWidth = 40;
                int filledWidth = (int)((percent / 100.0) * barWidth);

                string bar = new string('#', filledWidth) + new string('-', barWidth - filledWidth);
                string downloaded = FormatBytes(bytesDownloaded);
                string total = FormatBytes(totalBytes);
                string speed = speedMBps >= 1 ? $"{speedMBps:F1} MB/s" : $"{speedMBps * 1024:F0} KB/s";

                int currentTop = Console.CursorTop;
                Console.SetCursorPosition(0, _progressBarTop);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [{bar}] {percent,3}%  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{downloaded} / {total}  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{speed}   ");
                Console.ResetColor();

                Console.SetCursorPosition(0, currentTop);
            }
        }

        /// <summary>
        /// Обновляет прогресс-бар распаковки
        /// </summary>
        public static void UpdateExtractionProgressBar(int percent, int filesExtracted, int totalFiles, double speedFilesPerSec)
        {
            lock (_lock)
            {
                if (_progressBarTop < 0) return;

                int barWidth = 40;
                int filledWidth = (int)((percent / 100.0) * barWidth);

                string bar = new string('#', filledWidth) + new string('-', barWidth - filledWidth);
                string speed = speedFilesPerSec >= 1 ? $"{speedFilesPerSec:F0} files/s" : $"{speedFilesPerSec:F1} files/s";

                int currentTop = Console.CursorTop;
                Console.SetCursorPosition(0, _progressBarTop);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [{bar}] {percent,3}%  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{filesExtracted} / {totalFiles} files  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{speed}   ");
                Console.ResetColor();

                Console.SetCursorPosition(0, currentTop);
            }
        }

        /// <summary>
        /// Завершает прогресс-бар
        /// </summary>
        public static void FinishProgressBar()
        {
            lock (_lock)
            {
                if (_progressBarTop < 0) return;

                int currentTop = Console.CursorTop;
                Console.SetCursorPosition(0, _progressBarTop);

                string bar = new string('#', 40);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"  [{bar}] 100%  ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Download complete!                    ");
                Console.ResetColor();
                Console.WriteLine();

                _progressBarTop = -1;
            }
        }

        /// <summary>
        /// Завершает прогресс-бар распаковки
        /// </summary>
        public static void FinishExtractionProgressBar()
        {
            lock (_lock)
            {
                if (_progressBarTop < 0) return;

                int currentTop = Console.CursorTop;
                Console.SetCursorPosition(0, _progressBarTop);

                string bar = new string('#', 40);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"  [{bar}] 100%  ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Extraction complete!                  ");
                Console.ResetColor();
                Console.WriteLine();

                _progressBarTop = -1;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "??? MB";
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    public class ArgumentInfo
    {
        public string[] Names { get; set; }
        public string Description { get; set; }

        public ArgumentInfo(string[] names, string description)
        {
            Names = names;
            Description = description;
        }
    }
}
