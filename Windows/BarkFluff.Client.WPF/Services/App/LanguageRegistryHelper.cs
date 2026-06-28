using Microsoft.Win32;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Хранение выбранного языка интерфейса в реестре Windows.
    /// Зеркало <see cref="ThemeRegistryHelper"/>. Значения: "system" | "ru" | "en".
    /// </summary>
    public static class LanguageRegistryHelper
    {
        private const string RegistryPath = @"Software\BarkFluff";
        private const string LanguageValueName = "AppLanguage";
        private const string DefaultLanguage = "system";

        public static string GetLanguage()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(LanguageValueName) as string ?? DefaultLanguage;
        }

        public static void SetLanguage(string language)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(LanguageValueName, language, RegistryValueKind.String);
        }
    }
}
