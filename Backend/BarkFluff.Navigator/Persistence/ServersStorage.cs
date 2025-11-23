namespace BarkFluff.Navigator.Persistence;

using Domain;

using Microsoft.Extensions.Caching.Memory;

using System.Collections.Concurrent;

public class ServersStorage
{
    private readonly IMemoryCache _memoryCache;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, DateTime> _lastRegistrationTimes = new();
    private readonly TimeSpan _serverActivePeriod;
    private readonly TimeSpan _throttlePeriod;
    private const string ServersCacheKey = "ActiveServers";

    public ServersStorage(IMemoryCache memoryCache, IConfiguration configuration)
    {
        _memoryCache = memoryCache;
        _configuration = configuration;
        _serverActivePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ActivePeriodMinutes", 10));
        _throttlePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ThrottleMinutes", 2));
    }

    public Task<List<ServerInfo>> GetServers()
    {
        var now = DateTime.UtcNow;
        if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, (ServerInfo server, DateTime lastSeen)>>(ServersCacheKey, out var servers))
        {
            return Task.FromResult(new List<ServerInfo>());
        }

        var activeServers = servers.Values
            .Where(s => (now - s.lastSeen) <= _serverActivePeriod)
            .Select(s => s.server)
            .ToList();

        return Task.FromResult(activeServers);
    }

    public void RegisterServer(ServerInfo server)
    {
        var now = DateTime.UtcNow;
        var serverKey = $"{server.Name}:{server.BeaconHost}:{server.BeaconPort}";

        if (_lastRegistrationTimes.TryGetValue(serverKey, out var lastTime))
        {
            if (now - lastTime < _throttlePeriod)
            {
                throw new InvalidOperationException($"Регистрация сервера слишком частая. Повторите попытку через {(_throttlePeriod - (now - lastTime)).TotalSeconds:F0} секунд.");
            }
        }

        var servers = _memoryCache.GetOrCreate(ServersCacheKey, entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            return new ConcurrentDictionary<string, (ServerInfo, DateTime)>();
        });

        servers.AddOrUpdate(
            serverKey,
            (server, now),
            (key, existing) => (server, now)
        );

        _lastRegistrationTimes.AddOrUpdate(serverKey, now, (key, existing) => now);
    }
}