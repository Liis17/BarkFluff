namespace BarkFluff.Users.Domain;

// Кеш профиля пользователя чужой ноды (docs/rearch/01-addressing-identity.md).
// Источник истины — всегда домашний сервер; обновляется при резолве, входящих событиях
// (профильные изменения — этап 2.9) и по TTL.
public class RemoteUser
{
    public Guid Uuid { get; set; }

    public string Username { get; set; } = string.Empty;

    // punycode A-label lowercase (как KnownServer.ServerName в Federation).
    public string ServerName { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Bio { get; set; }

    // FileId на origin-сервере; рендер/проксирование — Фаза 3.
    public string? AvatarFileId { get; set; }

    public bool IsDeactivated { get; set; }

    public DateTime LastSyncedAt { get; set; }
}
