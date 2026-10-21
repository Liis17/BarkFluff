using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BarkFluff.Builder;

public enum NginxMode
{
    None,
    Compose,
    ExternalContainer,
    Host
}

public sealed class BuilderModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public string ImageChannel { get => Get("Nightly"); set => Set(value); }
    public string DockerNetworkName { get => Get("barkfluff-network"); set => Set(value); }
    public string AspNetCoreEnvironment { get => Get("Production"); set => Set(value); }
    public string PublicDomain { get => Get(""); set => Set(value); }
    public string NavigatorUrl { get => Get("https://navigator.barkfluff.com"); set => Set(value); }

    public string PostgresUser { get => Get("postgres"); set => Set(value); }
    public string PostgresPassword { get => Get(""); set => Set(value); }
    public string PostgresHostPort { get => Get(""); set => Set(value); }

    public string RabbitUser { get => Get("barkfluff"); set => Set(value); }
    public string RabbitPassword { get => Get(""); set => Set(value); }

    public bool IncludeSeq { get => Get(false); set => Set(value); }
    public string SeqAdminPassword { get => Get(""); set => Set(value); }
    public string SeqHostPort { get => Get("8081"); set => Set(value); }
    public string SeqApiKey { get => Get(""); set => Set(value); }

    public bool IncludeMinio { get => Get(true); set => Set(value); }
    public string MinioRootUser { get => Get("barkfluff"); set => Set(value); }
    public string MinioRootPassword { get => Get(""); set => Set(value); }
    public string MinioApiHostPort { get => Get("9000"); set => Set(value); }
    public string MinioConsoleHostPort { get => Get("9001"); set => Set(value); }
    public string S3Endpoint { get => Get("http://minio:9000"); set => Set(value); }

    public string FirebaseProjectId { get => Get(""); set => Set(value); }
    public string FirebasePrivateKeyId { get => Get(""); set => Set(value); }
    public string FirebasePrivateKey
    {
        get => Get("");
        set => Set((value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
    }
    public string FirebaseClientEmail { get => Get(""); set => Set(value); }
    public string FirebaseClientId { get => Get(""); set => Set(value); }

    public string WebPushVapidKey { get => Get(""); set => Set(value); }
    public string WebPushFirebaseApiKey { get => Get(""); set => Set(value); }
    public string WebPushFirebaseAuthDomain { get => Get(""); set => Set(value); }
    public string WebPushFirebaseProjectId { get => Get(""); set => Set(value); }
    public string WebPushFirebaseStorageBucket { get => Get(""); set => Set(value); }
    public string WebPushFirebaseMessagingSenderId { get => Get(""); set => Set(value); }
    public string WebPushFirebaseAppId { get => Get(""); set => Set(value); }

    public string TelegramBotToken { get => Get(""); set => Set(value); }
    public string TelegramOwnerId { get => Get(""); set => Set(value); }
    public string TelegramProxyUrl { get => Get(""); set => Set(value); }
    public string TelegramProxyUsername { get => Get(""); set => Set(value); }
    public string TelegramProxyPassword { get => Get(""); set => Set(value); }

    public string SmtpHost { get => Get(""); set => Set(value); }
    public string SmtpPort { get => Get("587"); set => Set(value); }
    public string SmtpSecurity { get => Get("Auto"); set => Set(value); }
    public string ImapHost { get => Get(""); set => Set(value); }
    public string ImapPort { get => Get("993"); set => Set(value); }
    public string ImapSecurity { get => Get("SslOnConnect"); set => Set(value); }
    public bool MailAcceptInvalidCertificates { get => Get(false); set => Set(value); }
    public string MailSupportPassword { get => Get(""); set => Set(value); }
    public string MailHelpPassword { get => Get(""); set => Set(value); }
    public string MailNoreplyPassword { get => Get(""); set => Set(value); }
    public string MailPrivacyPassword { get => Get(""); set => Set(value); }
    public string MailSecurityPassword { get => Get(""); set => Set(value); }

    public string LiveKitApiKey { get => Get(""); set => Set(value); }
    public string LiveKitApiSecret { get => Get(""); set => Set(value); }

    public string SetupHostPort { get => Get("7032"); set => Set(value); }
    public string SetupPublicOrigin { get => Get(""); set => Set(value); }
    public string SetupToken { get => Get(""); set => Set(value); }

    public NginxMode NginxMode { get => Get(NginxMode.None); set => Set(value); }
    public string NginxCertificateSourcePath { get => Get(""); set => Set(value); }
    public string NginxKeySourcePath { get => Get(""); set => Set(value); }
    public string NginxCertificatePath { get => Get("/etc/nginx/certs/barkfluff-crt.pem"); set => Set(value); }
    public string NginxKeyPath { get => Get("/etc/nginx/certs/barkfluff-key.pem"); set => Set(value); }
    public bool BehindCloudflare { get => Get(false); set => Set(value); }

    public bool IncludeLognara { get => Get(false); set => Set(value); }
    public string LognaraCoreUrl { get => Get(""); set => Set(value); }
    public string LognaraCoreToken { get => Get(""); set => Set(value); }
    public string LognaraServer { get => Get(""); set => Set(value); }
    public string LognaraBackend { get => Get("barkfluff"); set => Set(value); }
    public string LognaraEnvironment { get => Get(""); set => Set(value); }

    public ObservableCollection<BuilderLognaraTarget> LognaraApplications { get; } =
    [
        Target("SETUP", "Setup", "setup"),
        Target("SETUP_SETTINGS", "Setup Settings", "settings-setup"),
        Target("BEACON", "Beacon", "beacon"),
        Target("SETTINGS", "Settings", "settings"),
        Target("FILES", "Files", "files"),
        Target("IDENTITY", "Identity", "identity"),
        Target("MESSAGES", "Messages", "messages"),
        Target("NOTIFICATION", "Notification", "notification"),
        Target("USERS", "Users", "users"),
        Target("FAST_AUTH", "Fast Auth", "fast-auth"),
        Target("UPDATES", "Updates", "updates"),
        Target("ONLINER", "Onliner", "onliner"),
        Target("FEDERATION", "Federation", "federation"),
        Target("CLOUD_MESSAGING", "Cloud Messaging", "cloud-messaging"),
        Target("WEB", "Web", "web"),
        Target("DEVELOPERS", "Developers", "developers"),
        Target("CALLS", "Calls", "calls"),
        Target("BOTS", "Bots", "bots"),
        Target("ADMIN_PANEL", "Admin Panel", "admin-panel")
    ];

    public ObservableCollection<BuilderLognaraTarget> LognaraInfrastructure { get; } =
    [
        Target("POSTGRES", "PostgreSQL", "postgres"),
        Target("POSTGRES_BOOTSTRAP", "PostgreSQL Bootstrap", "postgres-bootstrap"),
        Target("SETUP_POSTGRES", "Setup PostgreSQL", "postgres-setup"),
        Target("SETUP_POSTGRES_BOOTSTRAP", "Setup PostgreSQL Bootstrap", "postgres-bootstrap-setup"),
        Target("RABBITMQ", "RabbitMQ", "rabbitmq"),
        Target("REDIS", "Redis", "redis"),
        Target("LIVEKIT", "LiveKit", "livekit"),
        Target("SEQ", "Seq", "seq"),
        Target("MINIO", "MinIO", "minio"),
        Target("NGINX", "Nginx", "nginx")
    ];

    public string OutputPath { get => Get(""); set => Set(value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetGlobalSeq(bool enabled)
    {
        IncludeSeq = enabled;
        foreach (var target in AllLognaraTargets())
            target.SeqEnable = enabled;
    }

    public void SetGlobalLognara(bool enabled)
    {
        IncludeLognara = enabled;
        foreach (var target in AllLognaraTargets())
            target.LognaraEnable = enabled;
    }

    public IEnumerable<BuilderLognaraTarget> AllLognaraTargets() =>
        LognaraApplications.Concat(LognaraInfrastructure);

    public BuilderLognaraTarget Application(string prefix) =>
        LognaraApplications.Single(x => x.Prefix == prefix);

    public BuilderLognaraTarget Infrastructure(string prefix) =>
        LognaraInfrastructure.Single(x => x.Prefix == prefix);

    private T Get<T>(T fallback, [CallerMemberName] string? propertyName = null) =>
        _values.TryGetValue(propertyName!, out var value) ? (T)value! : fallback;

    private void Set<T>(T value, [CallerMemberName] string? propertyName = null)
    {
        if (_values.TryGetValue(propertyName!, out var current)
            && EqualityComparer<T>.Default.Equals((T)current!, value))
            return;

        _values[propertyName!] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static BuilderLognaraTarget Target(string prefix, string displayName, string service) =>
        new(prefix, displayName, service);
}

public sealed class BuilderLognaraTarget(
    string prefix,
    string displayName,
    string serviceName) : INotifyPropertyChanged
{
    private bool _consoleOutput = true;
    private bool _seqEnable;
    private bool _lognaraEnable;
    private string _relayToken = "";

    public string Prefix { get; } = prefix;
    public string DisplayName { get; } = displayName;
    public string ServiceName { get; } = serviceName;
    public string RelayTokenVariable => $"{Prefix}_LOGNARA_RELAY_TOKEN";

    public bool ConsoleOutput { get => _consoleOutput; set => Set(ref _consoleOutput, value); }
    public bool SeqEnable { get => _seqEnable; set => Set(ref _seqEnable, value); }
    public bool LognaraEnable { get => _lognaraEnable; set => Set(ref _lognaraEnable, value); }
    public string RelayToken { get => _relayToken; set => Set(ref _relayToken, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class LognaraAgentToken
{
    public static string Normalize(string? value)
    {
        const string prefix = "LOGNARA_AGENT_TOKEN=";
        var token = (value ?? "").Trim();
        return token.StartsWith(prefix, StringComparison.Ordinal)
            ? token[prefix.Length..].Trim()
            : token;
    }
}
