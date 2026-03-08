using Barkfluff.Updater.CLI.UI;

using Microsoft.Win32;

namespace Barkfluff.Updater.CLI.Services
{
    /// <summary>
    /// Сервис для регистрации протокола barkfluff:// в реестре Windows
    /// </summary>
    public class ProtocolRegistrationService
    {
        private const string ProtocolName = "bf";
        private const string ProtocolDescription = "BarkFluff Messenger Protocol";

        /// <summary>
        /// Регистрирует протокол barkfluff:// для указанного исполняемого файла
        /// Требует прав администратора
        /// </summary>
        /// <param name="exePath">Путь к исполняемому файлу BarkFluff</param>
        public void RegisterProtocol(string exePath)
        {
            try
            {
                // Проверяем, совпадает ли текущий путь с зарегистрированным
                var registeredPath = GetRegisteredProtocolPath();
                if (!string.IsNullOrEmpty(registeredPath))
                {
                    // Нормализуем пути для сравнения
                    var normalizedExePath = NormalizePath(exePath);
                    var normalizedRegisteredPath = NormalizePath(registeredPath);

                    if (string.Equals(normalizedExePath, normalizedRegisteredPath, StringComparison.OrdinalIgnoreCase))
                    {
                        ConsoleUI.PrintProgress($"Protocol '{ProtocolName}://' is already registered with correct path");
                        return;
                    }

                    ConsoleUI.PrintProgress($"Protocol path mismatch detected. Updating registration...");
                    ConsoleUI.PrintProgress($"  Old: {registeredPath}");
                    ConsoleUI.PrintProgress($"  New: {exePath}");
                }
                else
                {
                    ConsoleUI.PrintProgress("Registering protocol handler...");
                }

                // HKEY_CLASSES_ROOT\bf
                using (var key = Registry.ClassesRoot.CreateSubKey(ProtocolName))
                {
                    key.SetValue("", $"URL:{ProtocolDescription}");
                    key.SetValue("URL Protocol", "");

                    // DefaultIcon
                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"\"{exePath}\",0");
                    }

                    // shell\open\command
                    using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                ConsoleUI.PrintSuccess($"Protocol '{ProtocolName}://' registered successfully");
            }
            catch (UnauthorizedAccessException)
            {
                ConsoleUI.PrintError("Failed to register protocol: Administrator rights required");
                throw;
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Failed to register protocol: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Получает путь к исполняемому файлу, зарегистрированному для протокола
        /// </summary>
        /// <returns>Путь к exe файлу или null если протокол не зарегистрирован</returns>
        private string GetRegisteredProtocolPath()
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey($@"{ProtocolName}\shell\open\command"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("")?.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            // Извлекаем путь из формата: "C:\Path\To\App.exe" "%1"
                            return ExtractPathFromCommand(value);
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки при чтении реестра
            }

            return null;
        }

        /// <summary>
        /// Извлекает путь к exe из командной строки реестра
        /// </summary>
        private string ExtractPathFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return null;

            // Если начинается с кавычки, ищем закрывающую кавычку
            if (command.StartsWith("\""))
            {
                var endQuoteIndex = command.IndexOf('\"', 1);
                if (endQuoteIndex > 0)
                {
                    return command.Substring(1, endQuoteIndex - 1);
                }
            }
            else
            {
                // Без кавычек - берем до первого пробела
                var spaceIndex = command.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    return command.Substring(0, spaceIndex);
                }
                return command;
            }

            return null;
        }

        /// <summary>
        /// Нормализует путь для сравнения (убирает лишние слэши, приводит к нижнему регистру)
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                return System.IO.Path.GetFullPath(path).ToLowerInvariant();
            }
            catch
            {
                return path.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Удаляет регистрацию протокола barkfluff://
        /// Требует прав администратора
        /// </summary>
        public void UnregisterProtocol()
        {
            try
            {
                ConsoleUI.PrintProgress("Unregistering protocol handler...");

                Registry.ClassesRoot.DeleteSubKeyTree(ProtocolName, false);

                ConsoleUI.PrintSuccess($"Protocol '{ProtocolName}://' unregistered successfully");
            }
            catch (UnauthorizedAccessException)
            {
                ConsoleUI.PrintError("Failed to unregister protocol: Administrator rights required");
                throw;
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Failed to unregister protocol: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Проверяет, зарегистрирован ли протокол
        /// </summary>
        public bool IsProtocolRegistered()
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(ProtocolName))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
