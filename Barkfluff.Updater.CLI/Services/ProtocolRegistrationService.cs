using Barkfluff.Updater.CLI.UI;

using Microsoft.Win32;

using System;

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
                ConsoleUI.PrintProgress("Registering protocol handler...");

                // HKEY_CLASSES_ROOT\barkfluff
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
