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
        return await _context.Servers.Where(s => s.LastSeenAt >= threshold).ToListAsync(ct);
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

    public async Task<ServerInfo?> GetByServerNameAsync(string serverName, CancellationToken ct = default)
    {
        var normalized = serverName.ToLowerInvariant();
        var threshold = DateTime.UtcNow - _serverActivePeriod;

        return await _context.Servers.FirstOrDefaultAsync(s => s.ServerName == normalized && s.LastSeenAt >= threshold, ct);
    }
}
