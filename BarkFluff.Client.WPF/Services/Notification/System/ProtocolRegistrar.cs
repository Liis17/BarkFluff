using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace BarkFluff.Client.WPF.Services.Notification.System
{
    public static class ProtocolRegistrar
    {
        // Единый метод для регистрации, принимает название протокола
        private static void RegisterProtocolInternal(string protocolName, string protocolDescription)
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "BarkFluff.Client.WPF.exe");

            // Формируем сложную команду для cmd.exe
            // Конструкция \"\\\" нужен, чтобы в реестр записались кавычки вокруг пути
            string commandPathEntry = $"\\\"{exePath}\\\" \\\"%1\\\"";

            var sb = new StringBuilder();
            sb.Append($"reg add \"HKCR\\{protocolName}\" /ve /d \"{protocolDescription}\" /f");
            sb.Append(" & ");
            sb.Append($"reg add \"HKCR\\{protocolName}\" /v \"URL Protocol\" /d \"\" /f");
            sb.Append(" & ");
            sb.Append($"reg add \"HKCR\\{protocolName}\\shell\\open\\command\" /ve /d \"{commandPathEntry}\" /f");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{sb}\"", // Запускаем цепочку команд
                Verb = "runas",             // Запрашиваем права админа (вызовет UAC один раз)
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden, // Скрываем окно консоли
                CreateNoWindow = true
            };

            try
            {
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                // Логирование ошибки или вывод сообщения, если пользователь нажал "Нет" в UAC
                MessageBox.Show("Не удалось зарегистрировать протокол. Возможно, отказано в правах.\n" + ex.Message);
            }
        }

        public static void RegisterBFProtocol()
        {
            RegisterProtocolInternal("bf", "URL:BF Protocol");
        }

        public static void RegisterDevBFProtocol()
        {
            RegisterProtocolInternal("bfdev", "URL:BF Dev Protocol");
        }
    }
}