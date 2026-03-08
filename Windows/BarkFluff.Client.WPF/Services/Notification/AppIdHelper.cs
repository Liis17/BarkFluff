using System.Runtime.InteropServices;

namespace BarkFluff.Client.WPF.Services.Notification
{
    public static class AppIdHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
    }
}
