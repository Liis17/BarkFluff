using Microsoft.Win32;

namespace BarkFluff.Client.WPF.Services.App
{
    public static class ThemeRegistryHelper
    {
        private const string RegistryPath = @"Software\BarkFluff";
        private const string ThemeValueName = "AppTheme";
        private const string DefaultTheme = "light";

        public static string GetTheme()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(ThemeValueName) as string ?? DefaultTheme;
        }

        public static void SetTheme(string theme)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(ThemeValueName, theme, RegistryValueKind.String);
        }
    }
}
