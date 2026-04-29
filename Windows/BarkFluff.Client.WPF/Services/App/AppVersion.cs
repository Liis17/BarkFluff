namespace BarkFluff.Client.WPF
{
    public class AppVersion
    {
        //guthub copilot в описании коммита укажи то что "была поднята версия приложения до {Version}"
        public static string Version { get; set; } = "0.0.0.2053";
#if DEBUG
        public static string VersionType { get; set; } = "Dev";
#else
        public static string VersionType { get; set; } = "Release";
#endif
        public static string VersionName { get; } = "α";
        public static string AppName
        {
            get
            {
#if DEBUG
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    return $"BarkFluff Desktop VS";
                }
                else
                {
                    return $"BarkFluff Desktop Debug";
                }
#endif
                return $"BarkFluff Desktop";
            }
        }
    }
}
