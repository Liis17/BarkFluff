using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Persistence.Services;

// Хранилище кеша remote-профилей (этап 2.1).
// Единая точка записи — используется резолвом (Features/ResolveFederatedUser) и серверным RPC
// UpsertRemoteUsers (от Federation). Правила пиннинга UUID к ServerName — здесь.
public class RemoteUsersStorage
{
    private readonly UsersContext _context;
    private readonly ILogger<RemoteUsersStorage> _logger;

    public RemoteUsersStorage(UsersContext context, ILogger<RemoteUsersStorage> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Поиск в кеше по (username, servername). Серверная часть обязана быть канонизирована (FidParser).
    /// </summary>
    public Task<RemoteUser?> GetAsync(string username, string serverName)
    {
        return _context.RemoteUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Username == username && r.ServerName == serverName);
    }

    /// <summary>
    /// Поиск в кеше по UUID.
    /// </summary>
    public Task<RemoteUser?> GetAsync(Guid uuid)
    {
        return _context.RemoteUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Uuid == uuid);
    }

    /// <summary>
    /// Батч-чтение remote-профилей по UUID.
    /// </summary>
    /// <summary>
    /// Есть ли remote-профиль с такой парой (нода, аватар) — anti-open-proxy для публичного
    /// маршрута аватаров (этап 3.4).
    /// </summary>
    public Task<bool> HasAvatarRefAsync(string serverName, string fileId)
    {
        return _context.RemoteUsers
            .AsNoTracking()
            .AnyAsync(u => u.ServerName == serverName && u.AvatarFileId == fileId);
    }

    public async Task<List<RemoteUser>> GetByUuidsAsync(IReadOnlyCollection<Guid> uuids)
    {
        if (uuids.Count == 0)
            return new List<RemoteUser>();

        return await _context.RemoteUsers
            .AsNoTracking()
            .Where(r => uuids.Contains(r.Uuid))
            .ToListAsync();
    }

    /// <summary>
    /// Результат попытки upsert.
    /// </summary>
    public enum UpsertStatus
    {
        Ok,
        /// <summary>UUID совпал с локальным пользователем этой ноды (удалённый «двойник»).</summary>
        RejectedLocalUuidCollision,
        /// <summary>UUID уже известен с другим ServerName (пиннинг UUID к ноде).</summary>
        RejectedServerNameMismatch,
    }

    public sealed record UpsertResult(UpsertStatus Status, RemoteUser? Record);

    /// <summary>
    /// Upsert с применением правил пиннинга (docs/rearch/01-addressing-identity.md).
    /// Конфликт UNIQUE (Username, ServerName) разрешается в пользу свежего резолва: найденная
    /// по (Username, ServerName) запись с ДРУГИМ UUID обновляется по новому UUID и данным.
    /// </summary>
    public async Task<UpsertResult> UpsertAsync(
        Guid uuid,
        string username,
        string serverName,
        string? firstName,
        string? lastName,
        string? bio,
        string? avatarFileId,
        bool isDeactivated,
        CancellationToken ct = default)
    {
        // 1) UUID не должен принадлежать локальному пользователю этой ноды.
        var localCollision = await _context.Users.AnyAsync(u => u.Uuid == uuid, ct);
        if (localCollision)
        {
            _logger.LogWarning(
                "RemoteUsers upsert отклонён (LocalUuidCollision): uuid {Uuid} принадлежит локальному пользователю, origin {ServerName}",
                uuid, serverName);
            return new UpsertResult(UpsertStatus.RejectedLocalUuidCollision, null);
        }

        // 2) UUID уже известен как remote, но с другим ServerName → пиннинг нарушен.
        var byUuid = await _context.RemoteUsers.FirstOrDefaultAsync(r => r.Uuid == uuid, ct);
        if (byUuid is not null && byUuid.ServerName != serverName)
        {
            _logger.LogWarning(
                "RemoteUsers upsert отклонён (ServerNameMismatch): uuid {Uuid} запиннен к {ExpectedServerName}, получен {ActualServerName}",
                uuid, byUuid.ServerName, serverName);
            return new UpsertResult(UpsertStatus.RejectedServerNameMismatch, byUuid);
        }

        // 3) Конфликт (Username, ServerName) с другим UUID — username освободился/занялся на origin.
        // Побеждает свежий резолв: старая запись (по старому UUID) удаляется, ниже создаётся новая.
        RemoteUser? reusableRecord = byUuid;
        var byUsernameServer = await _context.RemoteUsers
            .FirstOrDefaultAsync(r => r.Username == username && r.ServerName == serverName, ct);
        if (byUsernameServer is not null && byUsernameServer.Uuid != uuid)
        {
            // byUsernameServer и byUuid взаимно исключены (у byUuid тот же uuid, а у byUsernameServer — другой).
            _context.RemoteUsers.Remove(byUsernameServer);
            await _context.SaveChangesAsync(ct);
            reusableRecord = null; // ссылка удалена — ниже создадим новую запись с новым UUID.
        }

        var now = DateTime.UtcNow;
        if (reusableRecord is null)
        {
            var created = new RemoteUser
            {
                Uuid = uuid,
                Username = username,
                ServerName = serverName,
                FirstName = firstName,
                LastName = lastName,
                Bio = bio,
                AvatarFileId = avatarFileId,
                IsDeactivated = isDeactivated,
                LastSyncedAt = now,
            };
            _context.RemoteUsers.Add(created);
            await _context.SaveChangesAsync(ct);
            return new UpsertResult(UpsertStatus.Ok, created);
        }

        // byUuid выжил с тем же UUID/ServerName — обновляем профиль (при необходимости меняем username).
        reusableRecord.Username = username;
        reusableRecord.FirstName = firstName;
        reusableRecord.LastName = lastName;
        reusableRecord.Bio = bio;
        reusableRecord.AvatarFileId = avatarFileId;
        reusableRecord.IsDeactivated = isDeactivated;
        reusableRecord.LastSyncedAt = now;

        await _context.SaveChangesAsync(ct);
        return new UpsertResult(UpsertStatus.Ok, reusableRecord);
    }
}
