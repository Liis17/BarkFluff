namespace BarkFluff.Status.Catalog;

/// <summary>Публичный компонент ноды, за которым следит статус. Ключ совпадает с NodeSubdomains.</summary>
public sealed record PublicService(string Key, string Name);

public static class PublicServices
{
    // Порядок — порядок строк на странице. Files-media (files2), livekit, admin-panel и setup не публичные
    // ноды для статуса: медиа-хост относится к Files, остальные не входят в публичный контур.
    public static readonly IReadOnlyList<PublicService> All =
    [
        new("beacon", "Beacon"),
        new("bots", "Bots"),
        new("calls", "Calls"),
        new("developers", "Developers"),
        new("fast-auth", "FastAuth"),
        new("federation", "Federation"),
        new("files", "Files"),
        new("identity", "Identity"),
        new("messages", "Messages"),
        new("onliner", "Onliner"),
        new("updates", "Updates"),
        new("users", "Users"),
        new("web", "Web"),
    ];
}
