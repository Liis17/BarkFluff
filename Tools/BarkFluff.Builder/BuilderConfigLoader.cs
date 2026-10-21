using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BarkFluff.Builder;

public static class BuilderConfigLoader
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    public static BuilderModel Load(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Укажите папку конфигурации Builder.", nameof(directory));

        string root;
        try
        {
            root = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Не удалось открыть папку конфигурации '{directory}': {exception.Message}", exception);
        }

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Папка конфигурации Builder не найдена: '{root}'.");

        var compose = ReadRequired(root, "docker-compose.yml");
        var environment = ParseEnvironment(ReadRequired(root, ".env"));
        if (!environment.ContainsKey("BUILDER_IMAGE_CHANNEL") || !environment.ContainsKey("BUILDER_NGINX_MODE"))
            throw new InvalidDataException("Файл .env не содержит метаданные BarkFluff Builder; эта папка не поддерживается.");

        var services = ParseServices(compose);
        var channel = ParseImageChannel(compose);
        var configuredNginxMode = ParseNginxMode(environment["BUILDER_NGINX_MODE"]);
        var nginxMode = services.Contains("nginx") ? NginxMode.Compose : configuredNginxMode;
        if (!services.Contains("nginx") && configuredNginxMode == NginxMode.Compose)
            throw new InvalidDataException("Файл docker-compose.yml не содержит сервис nginx, хотя .env задаёт режим Compose.");

        var model = new BuilderModel
        {
            OutputPath = root,
            ImageChannel = channel,
            IncludeSeq = services.Contains("seq"),
            IncludeMinio = services.Contains("minio"),
            IncludeLognara = services.Contains("lognara-relay"),
            NginxMode = nginxMode
        };

        ApplyEnvironment(model, environment);
        model.SetupToken = ReadRequired(root, "secrets/setup_token");
        (model.LiveKitApiKey, model.LiveKitApiSecret) = ParseLiveKitConfig(ReadRequired(root, "livekit/livekit.yaml"));

        if (model.NginxMode == NginxMode.Compose)
        {
            model.NginxCertificateSourcePath = Path.Combine(root, "certs", "barkfluff-crt.pem");
            model.NginxKeySourcePath = Path.Combine(root, "certs", "barkfluff-key.pem");
            RequireFile(model.NginxCertificateSourcePath, "certs/barkfluff-crt.pem");
            RequireFile(model.NginxKeySourcePath, "certs/barkfluff-key.pem");
        }

        return model;
    }

    private static string ReadRequired(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        RequireFile(path, relativePath);
        try
        {
            return File.ReadAllText(path, Utf8WithoutBom);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new InvalidDataException($"Не удалось прочитать файл '{relativePath}': {exception.Message}", exception);
        }
    }

    private static void RequireFile(string path, string relativePath)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"В папке конфигурации Builder отсутствует обязательный файл '{relativePath}'.", path);
    }

    private static Dictionary<string, string> ParseEnvironment(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0 || !Regex.IsMatch(line[..equals], @"^[A-Z][A-Z0-9_]*$"))
                throw EnvironmentFormatError(lineIndex, "ожидалась строка KEY=\"значение\".");

            var key = line[..equals];
            var value = ParseQuotedEnvironmentValue(line[(equals + 1)..], lineIndex);
            if (!result.TryAdd(key, value))
                throw EnvironmentFormatError(lineIndex, $"ключ '{key}' указан повторно.");
        }

        return result;
    }

    private static string ParseQuotedEnvironmentValue(string source, int lineIndex)
    {
        if (source.Length < 2 || source[0] != '"')
            throw EnvironmentFormatError(lineIndex, "значение должно быть заключено в двойные кавычки.");

        var value = new StringBuilder();
        var closed = false;
        for (var index = 1; index < source.Length; index++)
        {
            var current = source[index];
            if (current == '"')
            {
                if (!string.IsNullOrWhiteSpace(source[(index + 1)..]))
                    throw EnvironmentFormatError(lineIndex, "после закрывающей кавычки есть лишний текст.");
                closed = true;
                break;
            }

            if (current == '\\')
            {
                if (++index >= source.Length)
                    throw EnvironmentFormatError(lineIndex, "незавершённая escape-последовательность.");
                value.Append(source[index] switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw EnvironmentFormatError(lineIndex, "неизвестная escape-последовательность.")
                });
                continue;
            }

            if (current == '$' && index + 1 < source.Length && source[index + 1] == '$')
            {
                value.Append('$');
                index++;
                continue;
            }

            value.Append(current);
        }

        if (!closed)
            throw EnvironmentFormatError(lineIndex, "не найдена закрывающая кавычка.");
        return value.ToString();
    }

    private static FormatException EnvironmentFormatError(int zeroBasedLine, string detail) =>
        new($"Файл .env, строка {zeroBasedLine + 1}: {detail}");

    private static HashSet<string> ParseServices(string compose)
    {
        var normalized = compose.Replace("\r\n", "\n", StringComparison.Ordinal);
        var servicesSection = Regex.Match(normalized, @"(?ms)^services:\s*\n(?<body>.*?)(?=^[A-Za-z][A-Za-z0-9_-]*:\s*$|\z)");
        if (!servicesSection.Success)
            throw new InvalidDataException("Файл docker-compose.yml не содержит раздел services.");

        return Regex.Matches(servicesSection.Groups["body"].Value, @"(?m)^  ([A-Za-z0-9_-]+):\s*$")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ParseImageChannel(string compose)
    {
        var imageNames = Regex.Matches(compose, @"(?m)^\s*image:\s*docker\.barkfluff\.com/barkfluff-(?<name>[A-Za-z0-9._-]+):latest\s*$")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        if (imageNames.Length == 0)
            throw new InvalidDataException("Файл docker-compose.yml не содержит образов BarkFluff Builder.");

        var channels = imageNames.Select(name => name.EndsWith("-nightly", StringComparison.Ordinal)
                ? "Nightly"
                : name.EndsWith("-dev", StringComparison.Ordinal) ? "Dev" : "Release")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (channels.Length != 1)
            throw new InvalidDataException("В docker-compose.yml смешаны образы разных каналов.");

        return channels[0];
    }

    private static NginxMode ParseNginxMode(string value)
    {
        if (Enum.TryParse<NginxMode>(value, ignoreCase: false, out var mode) && Enum.IsDefined(mode))
            return mode;
        throw new InvalidDataException("В .env указан неизвестный режим nginx в BUILDER_NGINX_MODE.");
    }

    private static void ApplyEnvironment(BuilderModel model, IReadOnlyDictionary<string, string> values)
    {
        Set("DOCKER_NETWORK_NAME", value => model.DockerNetworkName = value);
        Set("ASPNETCORE_ENVIRONMENT", value => model.AspNetCoreEnvironment = value);
        Set("NODE_PUBLIC_DOMAIN", value => model.PublicDomain = value);
        Set("NODE_NAVIGATOR_URL", value => model.NavigatorUrl = value);
        Set("POSTGRES_USER", value => model.PostgresUser = value);
        Set("POSTGRES_PASSWORD", value => model.PostgresPassword = value);
        Set("POSTGRES_HOST_PORT", value => model.PostgresHostPort = value);
        Set("RABBITMQ_DEFAULT_USER", value => model.RabbitUser = value);
        Set("RABBITMQ_DEFAULT_PASS", value => model.RabbitPassword = value);
        Set("SEQ_ADMIN_PASSWORD", value => model.SeqAdminPassword = value);
        Set("SEQ_HOST_PORT", value => model.SeqHostPort = value);
        Set("SEQ_API_KEY", value => model.SeqApiKey = value);
        Set("MINIO_ROOT_USER", value => model.MinioRootUser = value);
        Set("MINIO_ROOT_PASSWORD", value => model.MinioRootPassword = value);
        Set("MINIO_API_HOST_PORT", value => model.MinioApiHostPort = value);
        Set("MINIO_CONSOLE_HOST_PORT", value => model.MinioConsoleHostPort = value);
        Set("S3_ENDPOINT", value => model.S3Endpoint = value);
        Set("FIREBASE_PROJECT_ID", value => model.FirebaseProjectId = value);
        Set("FIREBASE_PRIVATE_KEY_ID", value => model.FirebasePrivateKeyId = value);
        Set("FIREBASE_PRIVATE_KEY", value => model.FirebasePrivateKey = value);
        Set("FIREBASE_CLIENT_EMAIL", value => model.FirebaseClientEmail = value);
        Set("FIREBASE_CLIENT_ID", value => model.FirebaseClientId = value);
        Set("WEB_PUSH_VAPID_KEY", value => model.WebPushVapidKey = value);
        Set("WEB_PUSH_FIREBASE_API_KEY", value => model.WebPushFirebaseApiKey = value);
        Set("WEB_PUSH_FIREBASE_AUTH_DOMAIN", value => model.WebPushFirebaseAuthDomain = value);
        Set("WEB_PUSH_FIREBASE_PROJECT_ID", value => model.WebPushFirebaseProjectId = value);
        Set("WEB_PUSH_FIREBASE_STORAGE_BUCKET", value => model.WebPushFirebaseStorageBucket = value);
        Set("WEB_PUSH_FIREBASE_MESSAGING_SENDER_ID", value => model.WebPushFirebaseMessagingSenderId = value);
        Set("WEB_PUSH_FIREBASE_APP_ID", value => model.WebPushFirebaseAppId = value);
        Set("TELEGRAM_BOT_TOKEN", value => model.TelegramBotToken = value);
        Set("TELEGRAM_ADMINS", value => model.TelegramOwnerId = value);
        Set("TELEGRAM_PROXY_URL", value => model.TelegramProxyUrl = value);
        Set("TELEGRAM_PROXY_USERNAME", value => model.TelegramProxyUsername = value);
        Set("TELEGRAM_PROXY_PASSWORD", value => model.TelegramProxyPassword = value);
        Set("MAIL_SMTP_HOST", value => model.SmtpHost = value);
        Set("MAIL_SMTP_PORT", value => model.SmtpPort = value);
        Set("MAIL_SMTP_SECURITY", value => model.SmtpSecurity = value);
        Set("MAIL_IMAP_HOST", value => model.ImapHost = value);
        Set("MAIL_IMAP_PORT", value => model.ImapPort = value);
        Set("MAIL_IMAP_SECURITY", value => model.ImapSecurity = value);
        Set("MAIL_ACCEPT_INVALID_CERTIFICATES", value => model.MailAcceptInvalidCertificates = ParseBoolean(value, "MAIL_ACCEPT_INVALID_CERTIFICATES"));
        Set("MAIL_SUPPORT_PASSWORD", value => model.MailSupportPassword = value);
        Set("MAIL_HELP_PASSWORD", value => model.MailHelpPassword = value);
        Set("MAIL_NOREPLY_PASSWORD", value => model.MailNoreplyPassword = value);
        Set("MAIL_PRIVACY_PASSWORD", value => model.MailPrivacyPassword = value);
        Set("MAIL_SECURITY_PASSWORD", value => model.MailSecurityPassword = value);
        Set("SETUP_HOST_PORT", value => model.SetupHostPort = value);
        Set("SETUP_PUBLIC_ORIGIN", value => model.SetupPublicOrigin = value);
        Set("LOGNARA_CORE_URL", value => model.LognaraCoreUrl = value);
        Set("LOGNARA_RELAY_CORE_TOKEN", value => model.LognaraCoreToken = value);
        Set("LOGNARA_SERVER", value => model.LognaraServer = value);
        Set("LOGNARA_BACKEND", value => model.LognaraBackend = value);
        Set("LOGNARA_ENVIRONMENT", value => model.LognaraEnvironment = value);
        Set("BUILDER_NGINX_CERTIFICATE_SOURCE_PATH", value => model.NginxCertificateSourcePath = value);
        Set("BUILDER_NGINX_KEY_SOURCE_PATH", value => model.NginxKeySourcePath = value);
        Set("BUILDER_NGINX_CERTIFICATE_PATH", value => model.NginxCertificatePath = value);
        Set("BUILDER_NGINX_KEY_PATH", value => model.NginxKeyPath = value);
        Set("BUILDER_NGINX_BEHIND_CLOUDFLARE", value => model.BehindCloudflare = ParseBoolean(value, "BUILDER_NGINX_BEHIND_CLOUDFLARE"));

        foreach (var target in model.LognaraApplications)
            ApplyLognaraTarget(target, "APPLICATION");
        foreach (var target in model.LognaraInfrastructure)
            ApplyLognaraTarget(target, "INFRASTRUCTURE");

        void Set(string key, Action<string> setter)
        {
            if (values.TryGetValue(key, out var value))
                setter(value);
        }

        bool ParseBoolean(string value, string key)
        {
            if (bool.TryParse(value, out var result))
                return result;
            throw new InvalidDataException($"В .env параметр {key} должен иметь значение true или false.");
        }

        void ApplyLognaraTarget(BuilderLognaraTarget target, string kind)
        {
            var prefix = $"BUILDER_LOGNARA_{kind}_{target.Prefix}_";
            Set(prefix + "CONSOLE_OUTPUT", value => target.ConsoleOutput = ParseBoolean(value, prefix + "CONSOLE_OUTPUT"));
            Set(prefix + "SEQ_ENABLE", value => target.SeqEnable = ParseBoolean(value, prefix + "SEQ_ENABLE"));
            Set(prefix + "LOGNARA_ENABLE", value => target.LognaraEnable = ParseBoolean(value, prefix + "LOGNARA_ENABLE"));
            Set(prefix + "SPOOL_PATH", value => target.SpoolPath = value);
            Set(target.RelayTokenVariable, value => target.RelayToken = value);
        }
    }

    private static (string ApiKey, string ApiSecret) ParseLiveKitConfig(string text)
    {
        var match = Regex.Match(text.Replace("\r\n", "\n", StringComparison.Ordinal),
            @"(?m)^  ""(?<key>(?:\\.|[^""\\])*)"": ""(?<secret>(?:\\.|[^""\\])*)""$");
        if (!match.Success)
            throw new InvalidDataException("Файл livekit/livekit.yaml не содержит ключ и secret в секции keys.");

        return (UnescapeYamlString(match.Groups["key"].Value), UnescapeYamlString(match.Groups["secret"].Value));
    }

    private static string UnescapeYamlString(string value)
    {
        var result = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\')
            {
                result.Append(value[index]);
                continue;
            }

            if (++index >= value.Length)
                throw new InvalidDataException("В livekit/livekit.yaml есть незавершённая escape-последовательность.");
            result.Append(value[index] switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                _ => throw new InvalidDataException("В livekit/livekit.yaml есть неизвестная escape-последовательность.")
            });
        }

        return result.ToString();
    }
}
