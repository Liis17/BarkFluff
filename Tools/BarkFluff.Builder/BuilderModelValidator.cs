using System.Globalization;
using System.IO;
using System.Net;

namespace BarkFluff.Builder;

public static class BuilderModelValidator
{
    private sealed record PortUse(string Name, int Start, int End, string Protocol);

    public static IReadOnlyList<string> Validate(BuilderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var errors = new List<string>();

        Require(model.PostgresPassword, "Пароль PostgreSQL обязателен.");
        Require(model.PostgresUser, "Пользователь PostgreSQL обязателен.");
        ValidateConnectionStringComponent(model.PostgresUser, "Пользователь PostgreSQL", errors);
        ValidateConnectionStringComponent(model.PostgresPassword, "Пароль PostgreSQL", errors);
        Require(model.RabbitPassword, "Пароль RabbitMQ обязателен.");
        Require(model.RabbitUser, "Пользователь RabbitMQ обязателен.");
        Require(model.SetupToken, "Токен setup обязателен.");
        Require(model.LiveKitApiKey, "LiveKit API key обязателен.");
        Require(model.LiveKitApiSecret, "LiveKit API secret обязателен.");
        if (model.IncludeSeq)
            Require(model.SeqAdminPassword, "Пароль администратора Seq обязателен, когда Seq включён.");
        if (model.IncludeMinio)
        {
            Require(model.MinioRootUser, "Пользователь MinIO обязателен, когда MinIO включён.");
            Require(model.MinioRootPassword, "Пароль MinIO обязателен, когда MinIO включён.");
        }

        if (!IsDnsName(model.PublicDomain))
            errors.Add("Публичный домен должен быть ASCII DNS-именем; для IDN введите punycode (например, xn--...).");
        ValidateHttpUrl(model.NavigatorUrl, "URL Navigator", required: true, errors);
        if (!model.IncludeMinio)
            ValidateHttpUrl(model.S3Endpoint, "S3 endpoint", required: true, errors);
        if (!string.IsNullOrWhiteSpace(model.SetupPublicOrigin))
            ValidateHttpUrl(model.SetupPublicOrigin, "Setup public origin", required: true, errors, originOnly: true);

        if (model.ImageChannel is not ("Nightly" or "Dev" or "Release"))
            errors.Add("Канал образов должен быть Nightly, Dev или Release.");
        if (string.IsNullOrWhiteSpace(model.DockerNetworkName)
            || !char.IsAsciiLetterOrDigit(model.DockerNetworkName[0])
            || !model.DockerNetworkName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            errors.Add("Имя Docker network содержит недопустимые символы.");

        Require(model.TelegramBotToken, "Токен Telegram bot обязателен.");
        if (!IsTelegramOwner(model.TelegramOwnerId))
            errors.Add("TELEGRAM_ADMINS должен содержать ровно одного владельца в формате id:username.");
        if (!string.IsNullOrWhiteSpace(model.TelegramProxyUrl)
            && (!Uri.TryCreate(model.TelegramProxyUrl, UriKind.Absolute, out var proxyUri)
                || proxyUri.Scheme is not ("http" or "https" or "socks5")
                || string.IsNullOrWhiteSpace(proxyUri.Host)
                || proxyUri.Host.Any(c => !char.IsAscii(c))))
            errors.Add("Telegram proxy URL должен быть корректным HTTP(S) или SOCKS5 URL; для IDN введите punycode.");

        if (!IsMailSecurity(model.SmtpSecurity) || !IsMailSecurity(model.ImapSecurity))
            errors.Add("Тип защиты SMTP/IMAP должен быть Auto, SslOnConnect, StartTls, StartTlsWhenAvailable или None.");

        if (!Enum.IsDefined(model.NginxMode))
            errors.Add("Неизвестный режим nginx.");
        if (model.NginxMode == NginxMode.Compose)
        {
            Require(model.NginxCertificateSourcePath, "Для nginx Compose укажите локальный файл сертификата.");
            Require(model.NginxKeySourcePath, "Для nginx Compose укажите локальный файл ключа.");
            if (!string.IsNullOrWhiteSpace(model.NginxCertificateSourcePath) && !File.Exists(model.NginxCertificateSourcePath))
                errors.Add("Файл сертификата nginx не найден.");
            if (!string.IsNullOrWhiteSpace(model.NginxKeySourcePath) && !File.Exists(model.NginxKeySourcePath))
                errors.Add("Файл ключа nginx не найден.");
        }
        else if (model.NginxMode is NginxMode.ExternalContainer or NginxMode.Host)
        {
            ValidateUnixPath(model.NginxCertificatePath, "Путь сертификата nginx", errors);
            ValidateUnixPath(model.NginxKeyPath, "Путь ключа nginx", errors);
        }

        ValidatePort(model.SetupHostPort, "Порт setup", required: true, errors);
        ValidatePort(model.PostgresHostPort, "Порт PostgreSQL", required: false, errors);
        ValidatePort(model.SmtpPort, "SMTP-порт", required: true, errors);
        ValidatePort(model.ImapPort, "IMAP-порт", required: true, errors);
        if (model.IncludeSeq)
            ValidatePort(model.SeqHostPort, "Порт Seq", required: true, errors);
        if (model.IncludeMinio)
        {
            ValidatePort(model.MinioApiHostPort, "Порт API MinIO", required: true, errors);
            ValidatePort(model.MinioConsoleHostPort, "Порт консоли MinIO", required: true, errors);
        }

        ValidatePortConflicts(model, errors);
        ValidateEnvironmentValues(model, errors);

        if (model.IncludeLognara)
            ValidateLognara(model, errors);

        return errors;

        void Require(string? value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(message);
        }
    }

    private static void ValidateLognara(BuilderModel model, ICollection<string> errors)
    {
        ValidateLognaraUrl(model.LognaraCoreUrl, errors);
        if (string.IsNullOrWhiteSpace(LognaraAgentToken.Normalize(model.LognaraCoreToken)))
            errors.Add("Токен Lognara Core обязателен, когда Lognara включён.");
        if (string.IsNullOrWhiteSpace(model.LognaraServer))
            errors.Add("Имя сервера Lognara обязательно, когда Lognara включён.");

        var active = ActiveLognaraTargets(model).ToArray();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in active)
        {
            var token = LognaraAgentToken.Normalize(target.RelayToken);
            if (string.IsNullOrWhiteSpace(token))
                errors.Add($"Токен relay обязателен для агента {target.DisplayName}.");
            else if (!tokens.Add(token))
                errors.Add("Токены relay активных Lognara-агентов должны быть уникальными.");

        }
    }

    private static IEnumerable<BuilderLognaraTarget> ActiveLognaraTargets(BuilderModel model)
    {
        foreach (var app in model.LognaraApplications)
        {
            if (app.Prefix is "SETUP" or "SETUP_SETTINGS" || model.LognaraApplications.IndexOf(app) > 1)
                if (app.LognaraEnable)
                    yield return app;
        }

        foreach (var target in model.LognaraInfrastructure)
        {
            var exists = target.Prefix switch
            {
                "SEQ" => model.IncludeSeq,
                "MINIO" => model.IncludeMinio,
                "NGINX" => model.NginxMode == NginxMode.Compose,
                _ => true
            };
            if (exists && target.LognaraEnable)
                yield return target;
        }
    }

    private static void ValidateLognaraUrl(string value, ICollection<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length != 0
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.TrimEnd('/').EndsWith("/v1/batches", StringComparison.Ordinal)
            || uri.Host.Any(c => !char.IsAscii(c)))
            errors.Add("Lognara Core URL должен быть HTTP(S) URL, оканчивающимся на /v1/batches; для IDN введите punycode.");
    }

    private static void ValidateHttpUrl(
        string value,
        string label,
        bool required,
        ICollection<string> errors,
        bool originOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors.Add($"{label} обязателен.");
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length != 0
            || !string.IsNullOrEmpty(uri.Fragment)
            || (originOnly && !string.IsNullOrEmpty(uri.Query)))
        {
            errors.Add($"{label} должен быть корректным HTTP(S) URL.");
            return;
        }

        if (uri.Host.Any(c => !char.IsAscii(c)))
        {
            errors.Add($"{label}: хост должен быть ASCII; для IDN введите punycode.");
            return;
        }

        if (originOnly && uri.AbsolutePath is not ("" or "/"))
            errors.Add($"{label} должен содержать только схему, хост и необязательный порт.");
    }

    private static bool IsDnsName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 253
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.Any(c => !char.IsAscii(c))
            || IPAddress.TryParse(value, out _))
            return false;

        try
        {
            var labels = value.Split('.');
            return labels.Length >= 2 && labels.All(label =>
                label.Length is > 0 and <= 63
                && label[0] != '-'
                && label[^1] != '-'
                && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsTelegramOwner(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && parts[1].Length > 0
            && parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static bool IsMailSecurity(string? value) => value?.Trim().ToLowerInvariant() is
        "auto" or "sslonconnect" or "starttls" or "starttlswhenavailable" or "none";

    private static void ValidateUnixPath(string path, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith("/", StringComparison.Ordinal)
            || path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ';' or '"' or '\'' or '{' or '}' or '#'))
            errors.Add($"{label} должен быть абсолютным Unix-путём без пробелов и управляющих символов.");
    }

    private static void ValidateConnectionStringComponent(string value, string label, ICollection<string> errors)
    {
        if (value.Any(c => c is ';' or '=' or '"' or '\'' || char.IsWhiteSpace(c)))
            errors.Add($"{label} не должен содержать ;, =, кавычки или пробелы, так как Settings формирует строку подключения PostgreSQL.");
    }

    private static void ValidatePort(string value, string label, bool required, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                errors.Add($"{label} обязателен.");
            return;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            errors.Add($"{label} должен быть числом от 1 до 65535.");
    }

    private static void ValidatePortConflicts(BuilderModel model, ICollection<string> errors)
    {
        var ports = new List<PortUse>();
        void Add(string name, string value, string protocol = "tcp")
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535)
                ports.Add(new PortUse(name, port, port, protocol));
        }
        void AddRange(string name, int start, int end, string protocol) => ports.Add(new PortUse(name, start, end, protocol));

        Add("PostgreSQL", model.PostgresHostPort);
        Add("Setup", model.SetupHostPort);
        if (model.IncludeSeq)
            Add("Seq", model.SeqHostPort);
        if (model.IncludeMinio)
        {
            Add("MinIO API", model.MinioApiHostPort);
            Add("MinIO console", model.MinioConsoleHostPort);
        }

        Add("LiveKit TCP", "7880");
        Add("LiveKit TCP", "7881");
        AddRange("LiveKit UDP", 50000, 50200, "udp");
        if (model.NginxMode != NginxMode.None)
        {
            Add("nginx HTTP", "80");
            Add("nginx HTTPS", "443");
        }
        if (model.NginxMode == NginxMode.Host)
        {
            foreach (var port in new[] { 7000, 7001, 7002, 7005, 7006, 7007, 7008, 7009, 7015, 7016, 7020, 7021, 7025, 7027, 7028, 7030, 51888 })
                Add("nginx Host upstream", port.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var group in ports.GroupBy(x => x.Protocol, StringComparer.Ordinal))
        {
            var items = group.ToArray();
            for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
            {
                if (items[i].Start <= items[j].End && items[j].Start <= items[i].End)
                    errors.Add($"Конфликт порта {group.Key.ToUpperInvariant()}: {items[i].Name} и {items[j].Name}.");
            }
        }
    }

    private static void ValidateEnvironmentValues(BuilderModel model, ICollection<string> errors)
    {
        foreach (var property in typeof(BuilderModel).GetProperties().Where(x => x.PropertyType == typeof(string)))
        {
            var allowsNewLines = property.Name == nameof(BuilderModel.FirebasePrivateKey);
            if (property.GetValue(model) is string value
                && value.Any(c => char.IsControl(c) && !(allowsNewLines && c == '\n')))
                errors.Add($"Поле {property.Name} содержит символ, который нельзя представить в .env.");
        }

        foreach (var target in model.AllLognaraTargets())
        {
            if (target.RelayToken.Any(char.IsControl))
                errors.Add($"Параметры Lognara-агента {target.DisplayName} содержат символ, который нельзя представить в .env.");
        }
    }
}
