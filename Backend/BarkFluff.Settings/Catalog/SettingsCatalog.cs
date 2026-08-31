using BarkFluff.Shared.Identity;

namespace BarkFluff.Settings.Catalog;

public sealed record SettingsCatalogEntry(
    ServiceId ServiceId,
    string Section,
    string Key,
    string StorageKey,
    Func<SettingsSeedValues, string> DefaultFactory,
    bool IsSensitive,
    bool RequiresManualValue)
{
    public SetupFieldMetadata? Setup { get; init; }
}

public sealed record SettingsSeedValues(
    string PostgresHost,
    string PostgresUsername,
    string PostgresPassword,
    string RabbitUsername,
    string RabbitPassword,
    string JwtSecret,
    string JwtIssuer,
    string JwtAudience,
    Func<string, string> ServiceTokenFactory);

public sealed class UnknownSettingException : InvalidOperationException
{
    public UnknownSettingException(ServiceId serviceId, string section, string key)
        : base($"Unknown settings key [{serviceId}] {section}:{key}.") { }
}

public static class SettingsCatalog
{
    private static readonly IReadOnlyDictionary<ServiceId, int> Ports = new Dictionary<ServiceId, int>
    {
        [ServiceId.Identity] = 7000, [ServiceId.Users] = 7001, [ServiceId.Beacon] = 7002,
        [ServiceId.Notifications] = 7004, [ServiceId.Files] = 7005, [ServiceId.Messages] = 7007,
        [ServiceId.FastAuth] = 7008, [ServiceId.Updates] = 7015, [ServiceId.Onliner] = 7009,
        [ServiceId.CloudMessaging] = 7011, [ServiceId.Web] = 7016, [ServiceId.Developers] = 7020,
        [ServiceId.Calls] = 7025, [ServiceId.Bots] = 7027, [ServiceId.Federation] = 7030
    };

    private static readonly IReadOnlyDictionary<ServiceId, string> Containers = new Dictionary<ServiceId, string>
    {
        [ServiceId.Identity] = "identity", [ServiceId.Users] = "users", [ServiceId.Beacon] = "beacon",
        [ServiceId.Notifications] = "notification", [ServiceId.Files] = "files", [ServiceId.Messages] = "messages",
        [ServiceId.FastAuth] = "fast-auth", [ServiceId.Updates] = "updates", [ServiceId.Onliner] = "onliner",
        [ServiceId.CloudMessaging] = "cloud-messaging", [ServiceId.Web] = "web", [ServiceId.Developers] = "developers",
        [ServiceId.Calls] = "calls", [ServiceId.Bots] = "bots", [ServiceId.Federation] = "federation"
    };

    private static readonly IReadOnlyDictionary<ServiceId, string> Databases = new Dictionary<ServiceId, string>
    {
        [ServiceId.Identity] = "identity", [ServiceId.Users] = "users", [ServiceId.Files] = "files",
        [ServiceId.Messages] = "messages", [ServiceId.Onliner] = "onliner", [ServiceId.Developers] = "developers",
        [ServiceId.Calls] = "calls", [ServiceId.Bots] = "bots", [ServiceId.Federation] = "federation"
    };

    public static IReadOnlyList<SettingsCatalogEntry> All { get; } = Build();

    private static readonly IReadOnlyDictionary<(ServiceId, string, string), SettingsCatalogEntry> ByLegacy =
        All.ToDictionary(entry => (entry.ServiceId, entry.Section, entry.Key));

    private static readonly IReadOnlyDictionary<(ServiceId, string), SettingsCatalogEntry> ByStorage =
        All.ToDictionary(entry => (entry.ServiceId, entry.StorageKey));

    public static SettingsCatalogEntry Resolve(ServiceId serviceId, string section, string key)
    {
        return ByLegacy.TryGetValue((serviceId, section, key), out var entry)
            ? entry
            : throw new UnknownSettingException(serviceId, section, key);
    }

    public static SettingsCatalogEntry Resolve(ServiceId serviceId, string storageKey)
    {
        return ByStorage.TryGetValue((serviceId, storageKey), out var entry)
            ? entry
            : throw new UnknownSettingException(serviceId, storageKey, string.Empty);
    }

    private static IReadOnlyList<SettingsCatalogEntry> Build()
    {
        var entries = new List<SettingsCatalogEntry>();

        AddDefault(entries, ServiceId.Unknown, "JwtSettings", "SecretKey", v => v.JwtSecret, true);
        AddDefault(entries, ServiceId.Unknown, "JwtSettings", "Issuer", v => v.JwtIssuer);
        AddDefault(entries, ServiceId.Unknown, "JwtSettings", "Audience", v => v.JwtAudience);
        AddLiteral(entries, ServiceId.Unknown, "JwtSettings", "ExpiryMinutes", "60");
        AddLiteral(entries, ServiceId.Unknown, "RabbitMQ", "Host", "rabbitmq");
        AddDefault(entries, ServiceId.Unknown, "RabbitMQ", "Username", v => v.RabbitUsername);
        AddDefault(entries, ServiceId.Unknown, "RabbitMQ", "Password", v => v.RabbitPassword, true);
        AddLiteral(entries, ServiceId.Unknown, "RabbitMQ", "VirtualHost", "/");
        AddServiceClient(entries, ServiceId.Unknown, "UsersService", ServiceId.Users);
        AddServiceClient(entries, ServiceId.Unknown, "FilesService", ServiceId.Files);
        AddServiceClient(entries, ServiceId.Unknown, "IdentityService", ServiceId.Identity);
        AddServiceClient(entries, ServiceId.Unknown, "BotsService", ServiceId.Bots);
        AddServiceClient(entries, ServiceId.Unknown, "FederationService", ServiceId.Federation);
        AddDefault(entries, ServiceId.Unknown, "SettingsService", "Host", _ => "http://settings:7003");

        AddServiceBase(entries, ServiceId.Identity, "IdentityDb");
        AddLiteral(entries, ServiceId.Identity, "Redis", "", "redis:6379");
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["HighRiskRequestsPerMinute"] = "60", ["SubjectRequestsPerWindow"] = "5",
            ["SubjectWindowMinutes"] = "15", ["FailureLimit"] = "5", ["FailureWindowMinutes"] = "15",
            ["LockoutMinutes"] = "15", ["CodeAttemptLimit"] = "5", ["OtpAttemptLimit"] = "5",
            ["BackoffBaseMilliseconds"] = "250", ["BackoffMaxMilliseconds"] = "2000"
        }) AddLiteral(entries, ServiceId.Identity, "IdentitySecurity", key, value);

        AddServiceBase(entries, ServiceId.Users, "UsersDb");
        AddServiceClient(entries, ServiceId.Users, "FilesService", ServiceId.Files);
        AddServiceClient(entries, ServiceId.Users, "MessagesService", ServiceId.Messages);

        AddServiceBase(entries, ServiceId.Beacon);
        AddLiteral(entries, ServiceId.Beacon, "NavigatorUrl", "", "http://navigator:7010");
        foreach (var key in new[] { "Name", "Description", "PublicName", "Location" }) AddManual(entries, ServiceId.Beacon, "ServerProps", key, SettingsSetupMetadata.Server(key));
        foreach (var key in new[] { "Lite", "Main", "Hard" }) AddManual(entries, ServiceId.Beacon, "ServerColor", key, SettingsSetupMetadata.Color(key));

        AddPort(entries, ServiceId.Notifications);
        foreach (var key in new[] { "Host", "Port", "SenderEmail", "SenderPassword" }) AddManual(entries, ServiceId.Notifications, "Email", key, SettingsSetupMetadata.Email(key));

        AddServiceBase(entries, ServiceId.Files, "FilesDb");
        AddLiteral(entries, ServiceId.Files, "RunSettings", "Http1Port", "7006");
        AddManual(entries, ServiceId.Files, "ExternalEndpoint", "MediaHost", SettingsSetupMetadata.Media());
        AddLiteral(entries, ServiceId.Files, "TempFiles", "ExpiresAt", "60");
        foreach (var bucket in new[] { "barkfluff-uploads", "profile-pictures", "message-documents", "message-videos", "message-images", "chat-pictures", "badge-images", "message-audio" })
        {
            var section = $"S3Buckets:{bucket}";
            AddLiteral(entries, ServiceId.Files, section, "ServiceUrl", "http://minio:9000");
            AddManual(entries, ServiceId.Files, section, "AccessKey", SettingsSetupMetadata.Storage(bucket, "AccessKey"));
            AddManual(entries, ServiceId.Files, section, "SecretKey", SettingsSetupMetadata.Storage(bucket, "SecretKey"));
            AddLiteral(entries, ServiceId.Files, section, "BucketName", bucket);
            AddLiteral(entries, ServiceId.Files, section, "Region", "auto");
        }
        AddServiceClient(entries, ServiceId.Files, "MessagesService", ServiceId.Messages);
        AddServiceClient(entries, ServiceId.Files, "FederationService", ServiceId.Federation);
        AddLiteral(entries, ServiceId.Files, "Files", "FedAvatarMaxBytes", "20971520");
        AddLiteral(entries, ServiceId.Files, "Files", "FedRetryAfterSeconds", "30");

        AddServiceBase(entries, ServiceId.Messages, "MessagesDb");
        AddServiceClient(entries, ServiceId.Messages, "FilesService", ServiceId.Files);
        AddLiteral(entries, ServiceId.Messages, "Redis", "", "redis:6379");

        AddServiceBase(entries, ServiceId.FastAuth);
        AddServiceClient(entries, ServiceId.FastAuth, "IdentityService", ServiceId.Identity);
        AddLiteral(entries, ServiceId.FastAuth, "Redis", "", "redis:6379");

        AddServiceBase(entries, ServiceId.Updates);

        AddServiceBase(entries, ServiceId.Onliner, "OnlinerDb");
        AddServiceClient(entries, ServiceId.Onliner, "MessagesService", ServiceId.Messages);
        AddServiceClient(entries, ServiceId.Onliner, "UsersService", ServiceId.Users);
        AddServiceClient(entries, ServiceId.Onliner, "FederationService", ServiceId.Federation);
        AddLiteral(entries, ServiceId.Onliner, "Redis", "", "redis:6379");
        AddLiteral(entries, ServiceId.Onliner, "Onliner", "RemotePresenceTtlSeconds", "900");
        AddLiteral(entries, ServiceId.Onliner, "Onliner", "PresenceInterestIntervalSeconds", "20");

        AddPort(entries, ServiceId.CloudMessaging);
        AddServiceClient(entries, ServiceId.CloudMessaging, "MessagesService", ServiceId.Messages);
        AddServiceClient(entries, ServiceId.CloudMessaging, "UsersService", ServiceId.Users);

        AddServiceBase(entries, ServiceId.Web);

        AddServiceBase(entries, ServiceId.Developers, "DevelopersDb");
        AddLiteral(entries, ServiceId.Developers, "RunSettings", "Http1Port", "7021");

        AddServiceBase(entries, ServiceId.Calls, "CallsDb");
        AddLiteral(entries, ServiceId.Calls, "RunSettings", "Http1Port", "7026");
        AddServiceClient(entries, ServiceId.Calls, "MessagesService", ServiceId.Messages);
        AddLiteral(entries, ServiceId.Calls, "LiveKit", "Url", "ws://livekit:7880");
        AddLiteral(entries, ServiceId.Calls, "LiveKit", "PublicUrl", "wss://calls.example.com");
        AddManual(entries, ServiceId.Calls, "LiveKit", "ApiKey", SettingsSetupMetadata.Calls("ApiKey"));
        AddManual(entries, ServiceId.Calls, "LiveKit", "ApiSecret", SettingsSetupMetadata.Calls("ApiSecret"));

        AddServiceBase(entries, ServiceId.Bots, "BotsDb");
        AddLiteral(entries, ServiceId.Bots, "RunSettings", "Http1Port", "7028");
        AddServiceClient(entries, ServiceId.Bots, "UsersService", ServiceId.Users);
        AddServiceClient(entries, ServiceId.Bots, "MessagesService", ServiceId.Messages);
        AddServiceClient(entries, ServiceId.Bots, "FilesService", ServiceId.Files);
        AddServiceClient(entries, ServiceId.Bots, "IdentityService", ServiceId.Identity);
        AddLiteral(entries, ServiceId.Bots, "Redis", "", "redis:6379");

        AddPort(entries, ServiceId.Federation);
        AddDatabase(entries, ServiceId.Federation, "FederationDb");
        AddSetupControl(entries, ServiceId.Federation, "Federation", "Enabled", "false", SettingsSetupMetadata.Federation("Enabled"));
        foreach (var key in new[] { "ServerName", "ExternalEndpoint", "TlsSpkiSha256", "WellKnownPort", "KeyRotationOverlapDays", "SignatureWindowSeconds" })
            AddManual(entries, ServiceId.Federation, "Federation", key, SettingsSetupMetadata.Federation(key));
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ChatCreatedHourlyLimit"] = "100", ["MaxPresenceSubscriptionSize"] = "500",
            ["PresenceInterestTtlSeconds"] = "60", ["PresenceReconcileSeconds"] = "10",
            ["PresenceResubscribeMinSeconds"] = "5", ["PresenceCoalesceSeconds"] = "5",
            ["PresenceResyncSeconds"] = "300", ["TypingCoalesceSeconds"] = "2",
            ["TypingDeadlineMs"] = "2000", ["TypingRateLimitPerOriginPerMinute"] = "600",
            ["TypingValidationCacheSeconds"] = "30", ["FetchFileRateLimitPerOrigin"] = "30",
            ["S2SConnectTimeout"] = "10", ["RemoteFileIdleTimeout"] = "60",
            ["RemoteFileCircuitFailures"] = "3", ["RemoteFileCircuitOpenSeconds"] = "60"
        }) AddLiteral(entries, ServiceId.Federation, "Federation", key, value);
        AddLiteral(entries, ServiceId.Federation, "NavigatorUrl", "", "http://navigator:7010");
        AddLiteral(entries, ServiceId.Federation, "Redis", "", "redis:6379");
        AddServiceClient(entries, ServiceId.Federation, "MessagesService", ServiceId.Messages);
        AddServiceClient(entries, ServiceId.Federation, "OnlinerService", ServiceId.Onliner);
        AddServiceClient(entries, ServiceId.Federation, "FilesService", ServiceId.Files);

        return entries;
    }

    private static void AddServiceBase(List<SettingsCatalogEntry> entries, ServiceId serviceId, string? databaseSection = null)
    {
        AddPort(entries, serviceId);
        AddDefault(entries, serviceId, "ExternalEndpoint", "Host", _ => $"https://{Containers[serviceId]}.example.com");
        if (databaseSection is not null) AddDatabase(entries, serviceId, databaseSection);
    }

    private static void AddPort(List<SettingsCatalogEntry> entries, ServiceId serviceId) =>
        AddDefault(entries, serviceId, "RunSettings", "Port", _ => Ports[serviceId].ToString());

    private static void AddDatabase(List<SettingsCatalogEntry> entries, ServiceId serviceId, string section) =>
        AddDefault(entries, serviceId, section, "", v => $"Host={v.PostgresHost};Database={Databases[serviceId]};Username={v.PostgresUsername};Password={v.PostgresPassword}", true);

    private static void AddServiceClient(List<SettingsCatalogEntry> entries, ServiceId owner, string section, ServiceId target)
    {
        AddDefault(entries, owner, section, "Host", _ => $"http://{Containers[target]}:{Ports[target]}");
        AddDefault(entries, owner, section, "Token", v => v.ServiceTokenFactory($"{section}Client"), true);
    }

    private static void AddLiteral(List<SettingsCatalogEntry> entries, ServiceId serviceId, string section, string key, string value, bool sensitive = false) =>
        AddDefault(entries, serviceId, section, key, _ => value, sensitive);

    private static void AddManual(List<SettingsCatalogEntry> entries, ServiceId serviceId, string section, string key, SetupFieldMetadata setup) =>
        entries.Add(Create(serviceId, section, key, _ => string.Empty, IsSensitive(section, key), true, setup));

    private static void AddSetupControl(List<SettingsCatalogEntry> entries, ServiceId serviceId, string section, string key, string value, SetupFieldMetadata setup) =>
        entries.Add(Create(serviceId, section, key, _ => value, IsSensitive(section, key), false, setup));

    private static void AddDefault(List<SettingsCatalogEntry> entries, ServiceId serviceId, string section, string key, Func<SettingsSeedValues, string> factory, bool sensitive = false) =>
        entries.Add(Create(serviceId, section, key, factory, sensitive || IsSensitive(section, key), false));

    private static SettingsCatalogEntry Create(ServiceId serviceId, string section, string key, Func<SettingsSeedValues, string> factory, bool sensitive, bool manual, SetupFieldMetadata? setup = null) =>
        new(serviceId, section, key, string.IsNullOrEmpty(key) ? section : $"{section}:{key}", factory, sensitive, manual)
        {
            Setup = setup
        };

    private static bool IsSensitive(string section, string key) =>
        key is "SecretKey" or "Password" or "Token" or "ApiSecret" or "ApiKey" or "AccessKey" or "SenderPassword"
        || section.EndsWith("Db", StringComparison.Ordinal);
}
