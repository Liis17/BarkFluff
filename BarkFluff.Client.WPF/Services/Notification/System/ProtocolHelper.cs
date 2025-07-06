using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Text;

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
