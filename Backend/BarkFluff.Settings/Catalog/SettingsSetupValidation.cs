using System.Globalization;
using System.Net;
using System.Net.Mail;

namespace BarkFluff.Settings.Catalog;

public sealed record SetupValidationResult(bool IsValid, string Value, string? Error)
{
    public static SetupValidationResult Success(string value) => new(true, value, null);

    public static SetupValidationResult Failure(string error, string? value = null) =>
        new(false, value ?? string.Empty, error);
}

public static class SettingsSetupValidation
{
    public static SetupValidationResult Validate(
        SettingsCatalogEntry entry,
        string? rawValue,
        string? currentValue = null)
    {
        var metadata = entry.Setup
            ?? throw new ArgumentException($"Catalog entry {entry.ServiceId}:{entry.StorageKey} is not a setup field.", nameof(entry));
        var value = rawValue ?? string.Empty;

        if (metadata.ValidatorId == "secret" && string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(currentValue))
            return SetupValidationResult.Success(currentValue);

        return metadata.ValidatorId switch
        {
            "server-name" => ValidateText(value, 1, 64, "Название сервера обязательно и должно содержать не более 64 символов."),
            "public-name" => ValidateText(value, 1, 64, "Публичное название обязательно и должно содержать не более 64 символов."),
            "description" => ValidateText(value, 1, 512, "Описание обязательно и должно содержать не более 512 символов."),
            "location" => ValidateText(value, 1, 128, "Расположение обязательно и должно содержать не более 128 символов."),
            "color" => ValidateColor(value),
            "smtp-host" => ValidateSmtpHost(value),
            "port" => ValidateInteger(value, 1, 65535, "Порт должен быть целым числом от 1 до 65535."),
            "email" => ValidateEmail(value),
            "access-key" => ValidateText(value, 1, 128, "Ключ доступа обязателен и должен содержать не более 128 символов."),
            "secret" => string.IsNullOrWhiteSpace(value)
                ? SetupValidationResult.Failure("Секретное значение не может быть пустым.")
                : SetupValidationResult.Success(value),
            "public-https-origin" => ValidateHttpsOrigin(value),
            "federation-server-name" => ValidateFederationServerName(value),
            "spki-sha256" => ValidateSpkiList(value),
            "rotation-days" => ValidateInteger(value, 1, 3650, "Окно ротации должно быть от 1 до 3650 дней."),
            "signature-window" => ValidateInteger(value, 1, 86400, "Окно подписи должно быть от 1 до 86400 секунд."),
            "boolean" => ValidateBoolean(value),
            _ => SetupValidationResult.Failure($"Для поля {entry.ServiceId}:{entry.StorageKey} не задан валидатор.")
        };
    }

    private static SetupValidationResult ValidateText(string value, int minLength, int maxLength, string error)
    {
        var normalized = value.Trim();
        return normalized.Length >= minLength && normalized.Length <= maxLength
            ? SetupValidationResult.Success(normalized)
            : SetupValidationResult.Failure(error, normalized);
    }

    private static SetupValidationResult ValidateColor(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
            return SetupValidationResult.Failure("Цвет должен быть в формате #RRGGBB.", value.Trim());

        return SetupValidationResult.Success($"#{normalized.ToUpperInvariant()}");
    }

    private static SetupValidationResult ValidateSmtpHost(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Any(char.IsWhiteSpace) || normalized.Contains('/')
            || normalized.Contains(':') && !IPAddress.TryParse(normalized, out _))
            return SetupValidationResult.Failure("SMTP host должен быть именем хоста или IP-адресом без схемы и порта.", normalized);

        var type = Uri.CheckHostName(normalized);
        return type is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? SetupValidationResult.Success(normalized)
            : SetupValidationResult.Failure("SMTP host имеет недопустимый формат.", normalized);
    }

    private static SetupValidationResult ValidateInteger(string value, int min, int max, string error)
    {
        var normalized = value.Trim();
        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= min && parsed <= max
            ? SetupValidationResult.Success(parsed.ToString(CultureInfo.InvariantCulture))
            : SetupValidationResult.Failure(error, normalized);
    }

    private static SetupValidationResult ValidateEmail(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            return SetupValidationResult.Failure("Укажите корректный email-адрес.", normalized);

        try
        {
            var address = new MailAddress(normalized);
            return string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase)
                ? SetupValidationResult.Success(normalized)
                : SetupValidationResult.Failure("Укажите один корректный email-адрес без отображаемого имени.", normalized);
        }
        catch (FormatException)
        {
            return SetupValidationResult.Failure("Укажите корректный email-адрес.", normalized);
        }
    }

    private static SetupValidationResult ValidateHttpsOrigin(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath is not ("" or "/"))
            return SetupValidationResult.Failure("Нужен публичный HTTPS-адрес без пути, query и fragment.", normalized);

        return SetupValidationResult.Success(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/'));
    }

    private static SetupValidationResult ValidateFederationServerName(string value)
    {
        var normalized = value.Trim();
        try
        {
            normalized = new IdnMapping().GetAscii(normalized).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return SetupValidationResult.Failure("Имя ноды не является корректным DNS-доменом.", value.Trim());
        }

        if (IPAddress.TryParse(normalized, out _) || normalized == "localhost" || Uri.CheckHostName(normalized) != UriHostNameType.Dns)
            return SetupValidationResult.Failure("Имя ноды должно быть DNS-доменом, а не IP или localhost.", normalized);

        return SetupValidationResult.Success(normalized);
    }

    private static SetupValidationResult ValidateSpkiList(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return SetupValidationResult.Failure("Укажите хотя бы один Base64 SPKI SHA-256 отпечаток.");

        var normalized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            try
            {
                var bytes = IsHexDigest(part)
                    ? Convert.FromHexString(part)
                    : Convert.FromBase64String(part);
                if (bytes.Length != 32)
                    return SetupValidationResult.Failure("Каждый SPKI SHA-256 отпечаток должен декодироваться ровно в 32 байта.");
                normalized.Add(Convert.ToBase64String(bytes));
            }
            catch (FormatException)
            {
                return SetupValidationResult.Failure("SPKI SHA-256 отпечатки должны быть корректными Base64-строками или 64-символьным hex.");
            }
        }

        return SetupValidationResult.Success(string.Join(',', normalized.Distinct(StringComparer.Ordinal)));
    }

    private static bool IsHexDigest(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static SetupValidationResult ValidateBoolean(string value) =>
        bool.TryParse(value.Trim(), out var parsed)
            ? SetupValidationResult.Success(parsed ? "true" : "false")
            : SetupValidationResult.Failure("Значение должно быть true или false.", value.Trim());
}
