using Microsoft.Win32;

namespace BarkFluff.Client.WPF.Services.Notification.System
{
    public static class ProtocolHelper
    {
        public static bool IsBFProtocolRegistered()
        {
            using (var key = Registry.ClassesRoot.OpenSubKey("bf"))
            {
                if (key == null) return false;

                var urlProtocol = key.GetValue("URL Protocol");
                return urlProtocol != null;
            }
        }
    }
}
