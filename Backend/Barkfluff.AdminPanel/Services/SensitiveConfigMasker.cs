using System.Text.RegularExpressions;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Маскирование чувствительных значений конфигурации перед отправкой в браузер.
/// </summary>
public static partial class SensitiveConfigMasker
{
    public const string MaskedValue = "••••••••";

    [GeneratedRegex("token|secret|password|accesskey|apikey", RegexOptions.IgnoreCase)]
    private static partial Regex SensitivePattern();

    public static bool IsSensitive(string section, string key) =>
        SensitivePattern().IsMatch(key)
        || SensitivePattern().IsMatch(section)
        || section.EndsWith("Db", StringComparison.OrdinalIgnoreCase);

    public static string MaskAccessKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Length <= 6)
            return MaskedValue;
        return $"{value[..3]}…{value[^2..]}";
    }
}
