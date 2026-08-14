using System.Collections.Concurrent;

using BarkFluff.FastAuth.Domain;
using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Tests.Fakes;

/// <summary>
/// In-memory реализация стора для тестов: та же машина состояний, что в Lua-скриптах
/// RedisFastAuthSessionStore (статус/код/юзер/срок проверяются атомарно под lock).
/// </summary>
public sealed class InMemoryFastAuthSessionStore : IFastAuthSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FastAuthSessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _subscribers = new();

    public FastAuthSessionState Create(string deviceName, string operationSystem,
        string appName, string appVersion, string ipAddress)
    {
        return CreateAsync(deviceName, operationSystem, appName, appVersion, ipAddress)
            .GetAwaiter().GetResult();
    }

    /// <summary>Сеет сессию с произвольным состоянием (истёкшая, отсканированная и т.д.).</summary>
    public void Seed(FastAuthSessionState session)
    {
        lock (_gate)
        {
            _sessions[session.Id] = session;
        }
    }

    public Task<FastAuthSessionState> CreateAsync(string deviceName, string operationSystem,
        string appName, string appVersion, string ipAddress, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var session = new FastAuthSessionState
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            ExpiresAt = now + FastAuthSessionTiming.SessionTtl,
            DeviceName = deviceName,
            OperationSystem = operationSystem,
            AppName = appName,
            AppVersion = appVersion,
            IpAddress = ipAddress
        };

        lock (_gate)
        {
            _sessions[session.Id] = session;
        }

        return Task.FromResult(session);
    }

    public Task<FastAuthSessionState?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_sessions.TryGetValue(id, out var session) ? session : null);
        }
    }

    public Task<FastAuthTransition> TryScanAsync(string id, long userId, string confirmationCode,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session)) return Task.FromResult(FastAuthTransition.NotFound);

            if (IsExpired(session))
            {
                MarkExpired(session);
                return Task.FromResult(FastAuthTransition.Expired);
            }

            if (session.Status != FastAuthStatus.Pending) return Task.FromResult(FastAuthTransition.InvalidState);

            _sessions[id] = session with
            {
                Status = FastAuthStatus.Scanned,
                UserId = userId,
                ConfirmationCode = confirmationCode
            };
            return Task.FromResult(FastAuthTransition.Ok);
        }
    }

    public Task<FastAuthTransition> TryAcceptAsync(string id, string confirmationCode, long userId,
        FastAuthSessionResult result, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session)) return Task.FromResult(FastAuthTransition.NotFound);

            if (IsExpired(session))
            {
                MarkExpired(session);
                return Task.FromResult(FastAuthTransition.Expired);
            }

            if (session.Status != FastAuthStatus.Scanned
                || session.ConfirmationCode != confirmationCode
                || session.UserId != userId)
            {
                return Task.FromResult(FastAuthTransition.InvalidState);
            }

            _sessions[id] = session with
            {
                Status = FastAuthStatus.Accepted,
                FinalizedAt = DateTime.UtcNow,
                Result = result
            };
            return Task.FromResult(FastAuthTransition.Ok);
        }
    }

    public Task<FastAuthTransition> TryRejectAsync(string id, string confirmationCode, long userId,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session)) return Task.FromResult(FastAuthTransition.NotFound);

            if (IsExpired(session))
            {
                MarkExpired(session);
                return Task.FromResult(FastAuthTransition.Expired);
            }

            if (session.Status != FastAuthStatus.Scanned
                || session.ConfirmationCode != confirmationCode
                || session.UserId != userId)
            {
                return Task.FromResult(FastAuthTransition.InvalidState);
            }

            _sessions[id] = session with
            {
                Status = FastAuthStatus.Rejected,
                FinalizedAt = DateTime.UtcNow,
                Result = new FastAuthSessionResult(FastAuthStatus.Rejected)
            };
            return Task.FromResult(FastAuthTransition.Ok);
        }
    }

    public Task<bool> TryExpireAsync(string id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session) || session.IsFinal)
            {
                return Task.FromResult(false);
            }

            MarkExpired(session);
            return Task.FromResult(true);
        }
    }

    public Task<string?> TryAttachSubscriberAsync(string id, TimeSpan lockTtl,
        CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        return Task.FromResult(_subscribers.TryAdd(id, token) ? token : null);
    }

    public Task ReleaseSubscriberAsync(string id, string ownerToken, CancellationToken ct = default)
    {
        ((ICollection<KeyValuePair<string, string>>)_subscribers)
            .Remove(new KeyValuePair<string, string>(id, ownerToken));
        return Task.CompletedTask;
    }

    public bool IsSubscriberAttached(string id) => _subscribers.ContainsKey(id);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    private static bool IsExpired(FastAuthSessionState session) =>
        !session.IsFinal && DateTime.UtcNow >= session.ExpiresAt;

    private void MarkExpired(FastAuthSessionState session)
    {
        _sessions[session.Id] = session with
        {
            Status = FastAuthStatus.Expired,
            FinalizedAt = DateTime.UtcNow,
            Result = new FastAuthSessionResult(FastAuthStatus.Expired)
        };
    }
}
