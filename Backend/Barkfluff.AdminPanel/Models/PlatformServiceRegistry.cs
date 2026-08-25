namespace Barkfluff.AdminPanel.Models;

/// <summary>
/// Сервис платформы: имя приложения в Seq, Docker-контейнер и базовый адрес liveness-пробы
/// GET /ping (null — у сервиса нет анонимного /ping, статус определяется по Docker и Seq).
/// probeHttp1: проба должна идти по HTTP/1.1 (listener с несколькими протоколами без ALPN
/// не принимает h2c prior-knowledge; gRPC-порты наоборот требуют h2c).
/// </summary>
public sealed record PlatformService(
    string Name,
    string Container,
    string? ProbeConfigKey = null,
    string? ProbeDefaultHost = null,
    bool ProbeHttp1 = false);

public static class PlatformServiceRegistry
{
    public static readonly IReadOnlyList<PlatformService> BarkFluff =
    [
        new("BarkFluff.Identity",       "identity",         "IdentityService:Host",       "http://identity:7000"),
        new("BarkFluff.Users",          "users",            "UsersService:Host",          "http://users:7001"),
        new("BarkFluff.Messages",       "messages",         "MessagesService:Host",        "http://messages:7007"),
        new("BarkFluff.Files",          "files",            "FilesService:Host",           "http://files:7005"),
        new("BarkFluff.Updates",        "updates",          "UpdatesService:Host",         "http://updates:7015"),
        new("BarkFluff.Notification",   "notification",     "NotificationService:Host",    "http://notification:7004"),
        new("BarkFluff.Beacon",         "beacon",           "BeaconService:Host",          "http://beacon:7002"),
        new("BarkFluff.FastAuth",       "fast-auth",        "FastAuthService:Host",        "http://fast-auth:7008"),
        new("BarkFluff.Onliner",        "onliner",          "OnlinerService:Host",         "http://onliner:7009"),
        new("BarkFluff.Federation",     "federation",       "FederationService:Host",      "http://federation:7030"),
        new("BarkFluff.CloudMessaging", "cloud-messaging"),
        new("BarkFluff.Web",            "web",              "WebService:Host",             "http://web:7016",       true),
        new("BarkFluff.Configuration",  "configuration",    "ConfigurationService:Host",   "http://configuration:7003"),
        new("BarkFluff.Developers",     "developers",       "DevelopersService:Host",      "http://developers:7020", true),
        new("BarkFluff.Calls",          "calls",            "CallsService:Host",           "http://calls:7025"),
        new("BarkFluff.Bots",           "bots",             "BotsService:Host",            "http://bots:7027"),
    ];

    public static readonly IReadOnlyList<PlatformService> Infrastructure =
    [
        new("Seq",        "seq"),
        new("Minio",      "minio"),
        new("RabbitMQ",   "rabbitmq"),
        new("Redis",      "redis"),
        new("PostgreSQL", "postgres_barkfluff"),
    ];

    public static readonly IReadOnlyList<PlatformService> All = [.. BarkFluff, .. Infrastructure];

    public static readonly IReadOnlyDictionary<string, string> ServiceToContainer =
        All.ToDictionary(s => s.Name, s => s.Container, StringComparer.OrdinalIgnoreCase);
}
