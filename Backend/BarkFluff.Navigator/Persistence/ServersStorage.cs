namespace BarkFluff.Navigator.Persistence;

using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

public class ServersStorage
{
    private readonly IMemoryCache memoryCache;
    private const string ServersCacheKey = "ActiveServers";
    private static readonly TimeSpan ServerActivePeriod = TimeSpan.FromMinutes(10);

    public ServersStorage(IMemoryCache memoryCache)
    {
        this.memoryCache = memoryCache;
    }

    public async Task<List<ServerInfo>> GetServers()
    {
        var now = DateTime.UtcNow;
        if (!memoryCache.TryGetValue<List<(ServerInfo server, DateTime lastSeen)>>(ServersCacheKey, out var servers))
        {
            return new List<ServerInfo>();
        }
        return servers.Where(s => (now - s.lastSeen) <= ServerActivePeriod).Select(s => s.server).ToList();
    }

    public void RegisterServer(ServerInfo server)
    {
        var now = DateTime.UtcNow;
        var servers = memoryCache.Get<List<(ServerInfo server, DateTime lastSeen)>>(ServersCacheKey) ?? new List<(ServerInfo, DateTime)>();
        var existing = servers.FindIndex(s => s.server.Name == server.Name && s.server.BeaconHost == server.BeaconHost && s.server.BeaconPort == server.BeaconPort);
        if (existing >= 0)
        {
            servers[existing] = (server, now);
        }
        else
        {
            servers.Add((server, now));
        }
        memoryCache.Set(ServersCacheKey, servers);
    }
}