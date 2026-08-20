namespace BarkFluff.Navigator.Persistence;

using Domain;

using Microsoft.EntityFrameworkCore;

public class ServersStorage
{
    private readonly NavigatorContext _context;
    private readonly RegistrationThrottle _throttle;
    private readonly TimeSpan _serverActivePeriod;

    public ServersStorage(NavigatorContext context, RegistrationThrottle throttle, IConfiguration configuration)
    {
        _context = context;
        _throttle = throttle;
        _serverActivePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ActivePeriodMinutes", 10));
    }

    public async Task<List<ServerInfo>> GetServersAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - _serverActivePeriod;
        // Ручные записи (IsManual) закреплены навсегда, авто-регистрации — только внутри TTL.
        return await _context.Servers.Where(s => s.IsManual || s.LastSeenAt >= threshold).ToListAsync(ct);
    }

    // Ключ идентичности: ServerName, если задан; иначе легаси Name+BeaconHost+BeaconPort (текущее поведение).
    public async Task RegisterServerAsync(ServerInfo server, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var throttleKey = !string.IsNullOrWhiteSpace(server.ServerName)
            ? server.ServerName!
            : $"{server.Name}:{server.BeaconHost}:{server.BeaconPort}";

        _throttle.CheckAndRecord(throttleKey);

        var existing = !string.IsNullOrWhiteSpace(server.ServerName)
            ? await _context.Servers.FirstOrDefaultAsync(s => s.ServerName == server.ServerName, ct)
            : await _context.Servers.FirstOrDefaultAsync(
                s => s.Name == server.Name && s.BeaconHost == server.BeaconHost && s.BeaconPort == server.BeaconPort, ct);

        if (existing == null)
        {
            server.CreatedAt = now;
            server.LastSeenAt = now;
            _context.Servers.Add(server);
        }
        else
        {
            existing.BeaconHost = server.BeaconHost;
            existing.BeaconPort = server.BeaconPort;
            existing.Name = server.Name;
            existing.Description = server.Description;
            existing.ServerPublicName = server.ServerPublicName;
            existing.Location = server.Location;
            existing.ColorLiteHex = server.ColorLiteHex;
            existing.ColorMainHex = server.ColorMainHex;
            existing.ColorHardHex = server.ColorHardHex;
            existing.AddedBy = server.AddedBy;
            existing.LastSeenAt = now;
            existing.ServerName = server.ServerName;
            existing.FederationEndpoint = server.FederationEndpoint;
            existing.TlsSpkiSha256 = server.TlsSpkiSha256;
            existing.FederationProtocolVersions = server.FederationProtocolVersions;
            existing.SigningKeys = server.SigningKeys;
            existing.WebEndpoint = server.WebEndpoint;
            existing.FilesMediaEndpoint = server.FilesMediaEndpoint;
        }

        await _context.SaveChangesAsync(ct);
    }

    // Ручная запись админа: закреплена в каталоге навсегда (вне TTL). ServerName пуст —
    // федеративная well-known валидация не применяется. Если позже реальная нода зарегистрируется
    // с тем же ключом идентичности (Name+BeaconHost+BeaconPort), upsert обновит эту же строку,
    // а IsManual сохранится — сервер останется закреплённым.
    public async Task AddManualServerAsync(ServerInfo server, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        server.IsManual = true;
        server.CreatedAt = now;
        server.LastSeenAt = now;
        server.ServerName = null;
        _context.Servers.Add(server);

        await _context.SaveChangesAsync(ct);
    }

    // Удалять можно только ручные записи: авто-регистрации не трогаем.
    public async Task<bool> DeleteManualServerAsync(long id, CancellationToken ct = default)
    {
        var server = await _context.Servers.FirstOrDefaultAsync(s => s.Id == id && s.IsManual, ct);
        if (server == null)
            return false;

        _context.Servers.Remove(server);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ServerInfo?> GetByServerNameAsync(string serverName, CancellationToken ct = default)
    {
        var normalized = serverName.ToLowerInvariant();
        var threshold = DateTime.UtcNow - _serverActivePeriod;

        return await _context.Servers.FirstOrDefaultAsync(s => s.ServerName == normalized && s.LastSeenAt >= threshold, ct);
    }
}
