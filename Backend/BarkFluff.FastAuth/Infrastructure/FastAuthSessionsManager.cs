using System.Collections.Concurrent;

using BarkFluff.FastAuth.Domain;

namespace BarkFluff.FastAuth.Infrastructure;

public class FastAuthSessionsManager
{
    public static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);

    /// <summary>Сколько держим финализированную сессию в памяти, чтобы успеть отдать финальное событие подписчику.</summary>
    public static readonly TimeSpan FinalRetention = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, FastAuthSession> _sessions = new();

    public FastAuthSession Create(string deviceName, string operationSystem,
        string appName, string appVersion, string ipAddress)
    {
        var now = DateTime.UtcNow;
        var session = new FastAuthSession
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            ExpiresAt = now + SessionTtl,
            DeviceName = deviceName,
            OperationSystem = operationSystem,
            AppName = appName,
            AppVersion = appVersion,
            IpAddress = ipAddress
        };

        _sessions[session.Id] = session;
        return session;
    }

    public FastAuthSession? TryGet(string id)
    {
        return _sessions.TryGetValue(id, out var session) ? session : null;
    }

    public bool Remove(string id) => _sessions.TryRemove(id, out _);

    public IReadOnlyCollection<FastAuthSession> Snapshot() => _sessions.Values.ToList();
}
