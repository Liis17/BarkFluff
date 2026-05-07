namespace BarkFluff.Navigator.Persistence;

using Domain;

using System.Collections.Concurrent;

public class ServersStorage
{
    private readonly ConcurrentDictionary<string, (ServerInfo server, DateTime lastSeen)> _servers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRegistrationTimes = new();
    private readonly TimeSpan _serverActivePeriod;
    private readonly TimeSpan _throttlePeriod;

    public ServersStorage(IConfiguration configuration)
    {
        _serverActivePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ActivePeriodMinutes", 10));
        _throttlePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ThrottleMinutes", 2));
    }

    public List<ServerInfo> GetServers()
    {
        var now = DateTime.UtcNow;
        return _servers.Values
            .Where(s => (now - s.lastSeen) <= _serverActivePeriod)
            .Select(s => s.server)
            .ToList();
    }

    public void RegisterServer(ServerInfo server)
    {
        var now = DateTime.UtcNow;
        var serverKey = $"{server.Name}:{server.BeaconHost}:{server.BeaconPort}";

        if (_lastRegistrationTimes.TryGetValue(serverKey, out var lastTime)
            && now - lastTime < _throttlePeriod)
        {
            var remainingSeconds = (_throttlePeriod - (now - lastTime)).TotalSeconds;
            throw new InvalidOperationException(
                $"Слишком частая регистрация сервера. Повторная попытка возможна через {remainingSeconds:F0} секунд."
            );
        }

        _servers.AddOrUpdate(
            serverKey,
            (server, now),
            (key, existing) => (server, now)
        );

        _lastRegistrationTimes[serverKey] = now;

        CleanupExpiredThrottleEntries(now);
    }

    private void CleanupExpiredThrottleEntries(DateTime now)
    {
        foreach (var entry in _lastRegistrationTimes)
        {
            if (now - entry.Value > _throttlePeriod)
            {
                _lastRegistrationTimes.TryRemove(entry.Key, out _);
            }
        }
    }
}
