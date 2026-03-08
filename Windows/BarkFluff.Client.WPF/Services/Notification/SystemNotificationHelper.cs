using System.Runtime.InteropServices;

namespace BarkFluff.Client.WPF.Services.Notification
{
    public class SystemNotificationHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SystemParametersInfo(int uAction, int uParam, ref bool lpvParam, int flags);

        private const int SPI_GETBEEP = 1;

        /// <summary>
        /// Проверить, включены ли системные звуки
        /// </summary>
        public static bool AreSystemSoundsEnabled()
        {
            bool isEnabled = false;
            SystemParametersInfo(SPI_GETBEEP, 0, ref isEnabled, 0);
            return isEnabled;
        }

        /// <summary>
        /// Воспроизвести системный звук уведомления
        /// </summary>
        public static void PlayNotificationSound()
        {
            try
            {
                //SystemSounds.Asterisk.Play();
            }
            catch
            {
                // Игнорируем ошибки воспроизведения звука
            }
        }
    }
}
