using System.Collections.Concurrent;

namespace BarkFluff.GrpcServer.XAuth;

public class TokenRevocationCache
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedSessions = new();

    public void Revoke(long userId, string deviceId, DateTime accessTokenExpiresAt)
    {
        var key = BuildKey(userId, deviceId);
        _revokedSessions[key] = accessTokenExpiresAt;
    }

    public bool IsRevoked(long userId, string deviceId)
    {
        var key = BuildKey(userId, deviceId);
        return _revokedSessions.ContainsKey(key);
    }

    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _revokedSessions)
        {
            if (kvp.Value < now)
            {
                _revokedSessions.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string BuildKey(long userId, string deviceId) => $"{userId}:{deviceId}";
}
