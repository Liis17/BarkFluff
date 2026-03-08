using Microsoft.Win32;

using System.IO;

namespace BarkFluff.Client.WPF.Services.Notification.System
{
    public static class ProtocolHelper
    {
        public static bool IsBFProtocolRegistered()
        {
            string expectedExePath = Path.Combine(AppContext.BaseDirectory, "BarkFluff.exe");

#if (!DEBUG)
            using (var key = Registry.ClassesRoot.OpenSubKey("bf"))
            {
                if (key == null) return false;

                var urlProtocol = key.GetValue("URL Protocol");
                if (urlProtocol == null) return false;

                // Проверяем путь к исполняемому файлу
                using (var commandKey = Registry.ClassesRoot.OpenSubKey("bf\\shell\\open\\command"))
                {
                    if (commandKey == null) return false;
                    
                    var commandValue = commandKey.GetValue("")?.ToString();
                    if (string.IsNullOrEmpty(commandValue)) return false;

                    // Извлекаем путь из командной строки (убираем кавычки и параметры)
                    var registeredPath = ExtractExecutablePath(commandValue);
                    
                    // Сравниваем пути (нормализуем для корректного сравнения)
                    return string.Equals(
                        Path.GetFullPath(registeredPath),
                        Path.GetFullPath(expectedExePath),
                        StringComparison.OrdinalIgnoreCase
                    );
                }
            }
#else
            using (var key = Registry.ClassesRoot.OpenSubKey("bfdev"))
            {
                if (key == null) return false;

                var urlProtocol = key.GetValue("URL Protocol");
                if (urlProtocol == null) return false;

                // Проверяем путь к исполняемому файлу
                using (var commandKey = Registry.ClassesRoot.OpenSubKey("bfdev\\shell\\open\\command"))
                {
                    if (commandKey == null) return false;

                    var commandValue = commandKey.GetValue("")?.ToString();
                    if (string.IsNullOrEmpty(commandValue)) return false;

                    // Извлекаем путь из командной строки (убираем кавычки и параметры)
                    var registeredPath = ExtractExecutablePath(commandValue);

                    // Сравниваем пути (нормализуем для корректного сравнения)
                    return string.Equals(
                        Path.GetFullPath(registeredPath),
                        Path.GetFullPath(expectedExePath),
                        StringComparison.OrdinalIgnoreCase
                    );
                }
            }
#endif
        }

        /// <summary>
        /// Извлекает путь к исполняемому файлу из командной строки реестра
        /// </summary>
        private static string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return string.Empty;

            // Если путь в кавычках, извлекаем содержимое между первыми кавычками
            if (commandLine.StartsWith("\""))
            {
                int endQuoteIndex = commandLine.IndexOf("\"", 1);
                if (endQuoteIndex > 0)
                {
                    return commandLine.Substring(1, endQuoteIndex - 1);
                }
            }

            // Если нет кавычек, берём первую часть до пробела
            int spaceIndex = commandLine.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return commandLine.Substring(0, spaceIndex);
            }

            return commandLine;
        }
    }
}
