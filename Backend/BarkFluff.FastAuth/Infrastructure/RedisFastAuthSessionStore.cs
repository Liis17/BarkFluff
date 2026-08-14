using System.Text.Json;

using BarkFluff.FastAuth.Domain;
using BarkFluff.Proto.FastAuth;

using StackExchange.Redis;

namespace BarkFluff.FastAuth.Infrastructure;

/// <summary>
/// Redis-реализация стора QR-сессий: ключ <c>fastauth:session:{id}</c>, TTL = SessionTtl + ExpirySlack
/// (значение остаётся читаемым после логического истечения — Expired отличим от NotFound).
/// Финализированная сессия живёт ещё FinalRetention, чтобы реконнект забрал результат.
/// Все переходы — Lua-скрипты: атомарная проверка статуса/кода/юзера/срока и запись одним шагом.
/// </summary>
public class RedisFastAuthSessionStore(IConnectionMultiplexer redis) : IFastAuthSessionStore
{
    private const string SessionKeyPrefix = "fastauth:session:";
    private const string SubscriberKeyPrefix = "fastauth:subscriber:";

    private static readonly TimeSpan FinalTtl = FastAuthSessionTiming.FinalRetention;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Default;

    // KEYS[1] — ключ сессии; ARGV[1] — nowUnixMs, ARGV[2] — finalTtlMs, ARGV[3] — userId, ARGV[4] — confirmationCode.
    private const string ScanScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 'NOT_FOUND' end
        local s = cjson.decode(raw)
        local now = tonumber(ARGV[1])
        if tonumber(s.ExpiresAtMs) <= now then
            if s.Status < 3 then
                s.Status = 5
                s.FinalizedAtMs = now
                s.Result = { Status = 5 }
                redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
            end
            return 'EXPIRED'
        end
        if s.Status ~= 1 then return 'INVALID' end
        s.Status = 2
        s.UserId = tonumber(ARGV[3])
        s.ConfirmationCode = ARGV[4]
        redis.call('SET', KEYS[1], cjson.encode(s), 'KEEPTTL')
        return 'OK'
        """;

    // KEYS[1] — ключ сессии; ARGV[1] — nowUnixMs, ARGV[2] — finalTtlMs, ARGV[3] — confirmationCode,
    // ARGV[4] — userId, ARGV[5] — JSON результата (с токенами).
    private const string AcceptScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 'NOT_FOUND' end
        local s = cjson.decode(raw)
        local now = tonumber(ARGV[1])
        if tonumber(s.ExpiresAtMs) <= now then
            if s.Status < 3 then
                s.Status = 5
                s.FinalizedAtMs = now
                s.Result = { Status = 5 }
                redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
            end
            return 'EXPIRED'
        end
        if s.Status ~= 2 then return 'INVALID' end
        if s.ConfirmationCode ~= ARGV[3] then return 'INVALID' end
        if s.UserId ~= tonumber(ARGV[4]) then return 'INVALID' end
        s.Status = 3
        s.FinalizedAtMs = now
        s.Result = cjson.decode(ARGV[5])
        redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
        return 'OK'
        """;

    // KEYS[1] — ключ сессии; ARGV[1] — nowUnixMs, ARGV[2] — finalTtlMs, ARGV[3] — confirmationCode, ARGV[4] — userId.
    private const string RejectScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 'NOT_FOUND' end
        local s = cjson.decode(raw)
        local now = tonumber(ARGV[1])
        if tonumber(s.ExpiresAtMs) <= now then
            if s.Status < 3 then
                s.Status = 5
                s.FinalizedAtMs = now
                s.Result = { Status = 5 }
                redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
            end
            return 'EXPIRED'
        end
        if s.Status ~= 2 then return 'INVALID' end
        if s.ConfirmationCode ~= ARGV[3] then return 'INVALID' end
        if s.UserId ~= tonumber(ARGV[4]) then return 'INVALID' end
        s.Status = 4
        s.FinalizedAtMs = now
        s.Result = { Status = 4 }
        redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
        return 'OK'
        """;

    // KEYS[1] — ключ сессии; ARGV[1] — nowUnixMs, ARGV[2] — finalTtlMs.
    private const string ExpireScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local s = cjson.decode(raw)
        if s.Status >= 3 then return 0 end
        s.Status = 5
        s.FinalizedAtMs = tonumber(ARGV[1])
        s.Result = { Status = 5 }
        redis.call('PSETEX', KEYS[1], ARGV[2], cjson.encode(s))
        return 1
        """;

    // KEYS[1] — ключ захвата подписчика; ARGV[1] — токен владельца.
    private const string ReleaseSubscriberScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private IDatabase Db => redis.GetDatabase();

    public async Task<FastAuthSessionState> CreateAsync(string deviceName, string operationSystem,
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

        await Db.StringSetAsync(SessionKey(session.Id), Serialize(ToStored(session)),
            FastAuthSessionTiming.SessionTtl + FastAuthSessionTiming.ExpirySlack);

        return session;
    }

    public async Task<FastAuthSessionState?> GetAsync(string id, CancellationToken ct = default)
    {
        var raw = await Db.StringGetAsync(SessionKey(id));
        return raw.IsNullOrEmpty ? null : FromStored(Deserialize(raw.ToString()));
    }

    public async Task<FastAuthTransition> TryScanAsync(string id, long userId, string confirmationCode,
        CancellationToken ct = default)
    {
        var result = await Db.ScriptEvaluateAsync(ScanScript,
            [SessionKey(id)],
            [NowMs(), FinalTtlMs, userId, confirmationCode]);

        return MapTransition(result);
    }

    public async Task<FastAuthTransition> TryAcceptAsync(string id, string confirmationCode, long userId,
        FastAuthSessionResult result, CancellationToken ct = default)
    {
        var stored = new StoredResult
        {
            Status = (int)result.Status,
            AccessToken = result.AccessToken,
            AccessTokenExpiresAtMs = ToUnixMs(result.AccessTokenExpiresAt),
            RefreshToken = result.RefreshToken,
            RefreshTokenExpiresAtMs = ToUnixMs(result.RefreshTokenExpiresAt)
        };

        var payload = await Db.ScriptEvaluateAsync(AcceptScript,
            [SessionKey(id)],
            [NowMs(), FinalTtlMs, confirmationCode, userId, JsonSerializer.Serialize(stored, Json)]);

        return MapTransition(payload);
    }

    public async Task<FastAuthTransition> TryRejectAsync(string id, string confirmationCode, long userId,
        CancellationToken ct = default)
    {
        var result = await Db.ScriptEvaluateAsync(RejectScript,
            [SessionKey(id)],
            [NowMs(), FinalTtlMs, confirmationCode, userId]);

        return MapTransition(result);
    }

    public async Task<bool> TryExpireAsync(string id, CancellationToken ct = default)
    {
        var result = await Db.ScriptEvaluateAsync(ExpireScript,
            [SessionKey(id)],
            [NowMs(), FinalTtlMs]);

        return (int)result == 1;
    }

    public async Task<string?> TryAttachSubscriberAsync(string id, TimeSpan lockTtl,
        CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var acquired = await Db.StringSetAsync(SubscriberKey(id), token, lockTtl, When.NotExists);
        return acquired ? token : null;
    }

    public Task ReleaseSubscriberAsync(string id, string ownerToken, CancellationToken ct = default)
    {
        return Db.ScriptEvaluateAsync(ReleaseSubscriberScript, [SubscriberKey(id)], [ownerToken]);
    }

    private static string SessionKey(string id) => SessionKeyPrefix + id;

    private static string SubscriberKey(string id) => SubscriberKeyPrefix + id;

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long FinalTtlMs => (long)FinalTtl.TotalMilliseconds;

    private static long? ToUnixMs(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(value.Value.ToUniversalTime()).ToUnixTimeMilliseconds() : null;

    private static DateTime? FromUnixMs(long? value) =>
        value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime : null;

    private static FastAuthTransition MapTransition(RedisResult result) => result.ToString() switch
    {
        "OK" => FastAuthTransition.Ok,
        "NOT_FOUND" => FastAuthTransition.NotFound,
        "EXPIRED" => FastAuthTransition.Expired,
        _ => FastAuthTransition.InvalidState
    };

    private static StoredSession ToStored(FastAuthSessionState s) => new()
    {
        Id = s.Id,
        CreatedAtMs = ToUnixMs(s.CreatedAt)!.Value,
        ExpiresAtMs = ToUnixMs(s.ExpiresAt)!.Value,
        DeviceName = s.DeviceName,
        OperationSystem = s.OperationSystem,
        AppName = s.AppName,
        AppVersion = s.AppVersion,
        IpAddress = s.IpAddress,
        Status = (int)s.Status,
        ConfirmationCode = s.ConfirmationCode,
        UserId = s.UserId,
        FinalizedAtMs = ToUnixMs(s.FinalizedAt),
        Result = s.Result is null ? null : new StoredResult
        {
            Status = (int)s.Result.Status,
            AccessToken = s.Result.AccessToken,
            AccessTokenExpiresAtMs = ToUnixMs(s.Result.AccessTokenExpiresAt),
            RefreshToken = s.Result.RefreshToken,
            RefreshTokenExpiresAtMs = ToUnixMs(s.Result.RefreshTokenExpiresAt)
        }
    };

    private static FastAuthSessionState FromStored(StoredSession s) => new()
    {
        Id = s.Id,
        CreatedAt = FromUnixMs(s.CreatedAtMs)!.Value,
        ExpiresAt = FromUnixMs(s.ExpiresAtMs)!.Value,
        DeviceName = s.DeviceName,
        OperationSystem = s.OperationSystem,
        AppName = s.AppName,
        AppVersion = s.AppVersion,
        IpAddress = s.IpAddress,
        Status = (FastAuthStatus)s.Status,
        ConfirmationCode = s.ConfirmationCode,
        UserId = s.UserId,
        FinalizedAt = FromUnixMs(s.FinalizedAtMs),
        Result = s.Result is null ? null : new FastAuthSessionResult(
            (FastAuthStatus)s.Result.Status,
            s.Result.AccessToken,
            FromUnixMs(s.Result.AccessTokenExpiresAtMs),
            s.Result.RefreshToken,
            FromUnixMs(s.Result.RefreshTokenExpiresAtMs))
    };

    private static string Serialize(StoredSession session) => JsonSerializer.Serialize(session, Json);

    private static StoredSession Deserialize(string json) => JsonSerializer.Deserialize<StoredSession>(json)!;

    /// <summary>Сериализуемое состояние. DateTime — unix ms: Lua не умеет парсить ISO 8601.</summary>
    private sealed class StoredSession
    {
        public string Id { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
        public long ExpiresAtMs { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string OperationSystem { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Status { get; set; } = (int)FastAuthStatus.Pending;
        public string? ConfirmationCode { get; set; }
        public long? UserId { get; set; }
        public long? FinalizedAtMs { get; set; }
        public StoredResult? Result { get; set; }
    }

    private sealed class StoredResult
    {
        public int Status { get; set; }
        public string? AccessToken { get; set; }
        public long? AccessTokenExpiresAtMs { get; set; }
        public string? RefreshToken { get; set; }
        public long? RefreshTokenExpiresAtMs { get; set; }
    }
}
