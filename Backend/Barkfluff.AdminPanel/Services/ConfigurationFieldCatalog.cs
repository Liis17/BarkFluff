using System.Globalization;

namespace Barkfluff.AdminPanel.Services;

public enum ConfigurationFieldType
{
    String,
    Secret,
    Boolean,
    Integer,
    Url
}

public sealed record ConfigurationFieldDefinition(
    ConfigurationFieldType Type,
    bool Required = false,
    long? Minimum = null,
    long? Maximum = null,
    string? Hint = null);

/// <summary>
/// Описывает тип и ограничения существующих строк конфигурации без отдельной схемы в БД.
/// Правила намеренно консервативны: неизвестные ключи остаются строками.
/// </summary>
public static class ConfigurationFieldCatalog
{
    private static readonly string[] BooleanKeySuffixes = ["Enabled", "UseSsl", "UseTls"];
    private static readonly string[] IntegerKeyParts =
        ["Count", "Limit", "Size", "Attempts", "Minutes", "Seconds", "Milliseconds", "Days", "Ttl"];

    public static ConfigurationFieldDefinition Describe(string section, string key, string currentValue)
    {
        if (SensitiveConfigMasker.IsSensitive(section, key))
            return new(ConfigurationFieldType.Secret, Required: true, Hint: "Секрет хранится и передаётся только в скрытом виде");

        if (bool.TryParse(currentValue, out _) ||
            BooleanKeySuffixes.Any(suffix => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return new(ConfigurationFieldType.Boolean, Required: true);

        if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("Port", StringComparison.OrdinalIgnoreCase))
        {
            return new(ConfigurationFieldType.Integer, Required: true, Minimum: 1, Maximum: 65535, Hint: "Допустимый порт: 1–65535");
        }

        if (IsUrlField(section, key, currentValue))
            return new(ConfigurationFieldType.Url, Hint: "Абсолютный HTTP(S)-адрес; пустое значение отключает override");

        if (long.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            IntegerKeyParts.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase)))
        {
            return new(ConfigurationFieldType.Integer, Required: true, Hint: "Целое число");
        }

        return new(ConfigurationFieldType.String);
    }

    public static string? Validate(ConfigurationFieldDefinition field, string value)
    {
        if (value.Length > 8192)
            return "Значение не должно быть длиннее 8192 символов";

        if (string.IsNullOrWhiteSpace(value))
            return field.Required ? "Значение обязательно" : null;

        switch (field.Type)
        {
            case ConfigurationFieldType.Boolean when !bool.TryParse(value, out _):
                return "Допустимы только true или false";

            case ConfigurationFieldType.Integer:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return "Введите целое число";
                if (field.Minimum.HasValue && number < field.Minimum.Value)
                    return $"Значение должно быть не меньше {field.Minimum.Value}";
                if (field.Maximum.HasValue && number > field.Maximum.Value)
                    return $"Значение должно быть не больше {field.Maximum.Value}";
                break;

            case ConfigurationFieldType.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return "Введите абсолютный HTTP(S)-адрес";
                }
                break;
        }

        return null;
    }

    private static bool IsUrlField(string section, string key, string currentValue)
    {
        if (key.EndsWith("Url", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("Uri", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Endpoint", StringComparison.OrdinalIgnoreCase) ||
            (section.Contains("Endpoint", StringComparison.OrdinalIgnoreCase) &&
             key.EndsWith("Host", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Uri.TryCreate(currentValue, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
