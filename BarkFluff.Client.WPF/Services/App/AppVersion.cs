namespace BarkFluff.Client.WPF
{
    public class AppVersion
    {
        public static string Version { get; set; } = "0.0.0.1382";
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
                    Console.WriteLine("BarkFluff on Visual studio");
                    return "BarkFluff on Visual studio";
                }
#endif
                return "BarkFluff";
            }
        }
    }
}
