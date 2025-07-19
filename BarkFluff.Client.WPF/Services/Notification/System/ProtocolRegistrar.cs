using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace BarkFluff.Client.WPF.Services.Notification.System
{
    public static class ProtocolRegistrar
    {
        public static void RegisterBFProtocol()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            if (exePath.EndsWith(".dll"))
            {
                exePath = exePath.Replace(".dll", ".exe");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"add \"HKCR\\bf\" /ve /d \"URL:BF Protocol\" /f",
                Verb = "runas",
                UseShellExecute = true,
            };

            try
            {
                Process.Start(psi)?.WaitForExit();

                psi.Arguments = $"add \"HKCR\\bf\" /v \"URL Protocol\" /d \"\" /f";
                Process.Start(psi)?.WaitForExit();

                string commandPath = $"\"{exePath}\" \"%1\"";
                psi.Arguments = $"add \"HKCR\\bf\\shell\\open\\command\" /ve /d \"{commandPath}\" /f";
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка регистрации протокола: " + ex.Message);
            }
        }
    }
}
