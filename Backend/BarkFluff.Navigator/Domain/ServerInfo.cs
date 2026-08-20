namespace BarkFluff.Navigator.Domain;

using System.ComponentModel.DataAnnotations;

public class ServerInfo
{
    [Key]
    public long Id { get; set; }
    public required string BeaconHost { get; set; }
    public int BeaconPort { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string ServerPublicName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ColorLiteHex { get; set; } = string.Empty;
    public string ColorMainHex { get; set; } = string.Empty;
    public string ColorHardHex { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public required string AddedBy { get; set; }

    // Этап 1.5 rearch — персистентность + federation-поля (docs/rearch/03-discovery.md, "Источник 2").
    public DateTime LastSeenAt { get; set; }
    public string? ServerName { get; set; }
    public string? FederationEndpoint { get; set; }
    public string[]? TlsSpkiSha256 { get; set; }
    public int[]? FederationProtocolVersions { get; set; }
    public List<NavigatorSigningKeyInfo>? SigningKeys { get; set; }

    // gRPC-Web шлюз ноды для браузера (BarkFluff.Web). Пусто — нода не поддерживает веб-клиент.
    public string? WebEndpoint { get; set; }

    // Отдельный origin файлового HTTP в обход CDN. Пусто — файлы отдаются только по адресу Files.
    public string? FilesMediaEndpoint { get; set; }

    // Ручная запись администратора: всегда видна в каталоге (ListServers, админка, публичная страница),
    // вне TTL активной регистрации. Добавляется через админку Navigator.
    public bool IsManual { get; set; }
}

public class NavigatorSigningKeyInfo
{
    public string KeyId { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public DateTime? ExpiredAt { get; set; }
}
