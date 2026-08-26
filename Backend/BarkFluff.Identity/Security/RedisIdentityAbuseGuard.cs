using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Settings;
using BarkFluff.Shared.Exceptions.Identity;

using StackExchange.Redis;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Identity.Security;

public sealed class RedisIdentityAbuseGuard : IIdentityAbuseGuard
{
    private const string KeyPrefix = "barkfluff:identity";

    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    private const string LoginFailureScript = """
        if redis.call('EXISTS', KEYS[3]) == 1 or redis.call('EXISTS', KEYS[4]) == 1 then
            return -1
        end

        local loginCount = redis.call('INCR', KEYS[1])
        if loginCount == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end

        local userCount = redis.call('INCR', KEYS[2])
        if userCount == 1 then redis.call('EXPIRE', KEYS[2], ARGV[1]) end

        local limit = tonumber(ARGV[2])
        if loginCount >= limit or userCount >= limit then
            redis.call('SET', KEYS[3], '1', 'EX', ARGV[3], 'NX')
            redis.call('SET', KEYS[4], '1', 'EX', ARGV[3], 'NX')
            return -2
        end

        if loginCount > userCount then return loginCount else return userCount end
        """;

    private const string LoginFailureWithoutUserScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then
            return -1
        end

        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end

        if count >= tonumber(ARGV[2]) then
            redis.call('SET', KEYS[2], '1', 'EX', ARGV[3], 'NX')
            return -2
        end

        return count
        """;

    private const string FailureScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then
            return -1
        end

        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end

        if count >= tonumber(ARGV[2]) then
            redis.call('SET', KEYS[2], '1', 'EX', ARGV[3], 'NX')
            return -2
        end

        return count
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly IdentitySecurityOptions _options;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<RedisIdentityAbuseGuard> _logger;

    public RedisIdentityAbuseGuard(
        IConnectionMultiplexer redis,
        IdentitySecurityOptions options,
        MetricsCollector metrics,
        ILogger<RedisIdentityAbuseGuard> logger)
    {
        _redis = redis;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task EnsureRequestAllowedAsync(
        IdentityAbuseOperation operation,
        string? trustedIpAddress,
        string? subject,
        bool countSubject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestCount = await IncrementAsync(
            RequestKey(trustedIpAddress),
            TimeSpan.FromMinutes(1),
            cancellationToken);

        if (requestCount > Positive(_options.HighRiskRequestsPerMinute, 60))
            ThrowRateLimit(operation, trustedIpAddress);

        if (countSubject && !string.IsNullOrWhiteSpace(subject))
            await EnsureSubjectRequestAllowedAsync(operation, subject, cancellationToken);
    }

    public async Task EnsureSubjectRequestAllowedAsync(
        IdentityAbuseOperation operation,
        string subject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return;

        var limit = Positive(_options.SubjectRequestsPerWindow, 5);
        var count = await IncrementAsync(
            SubjectRequestKey(subject),
            TimeSpan.FromMinutes(Positive(_options.SubjectWindowMinutes, 15)),
            cancellationToken);

        if (count > limit)
            ThrowRateLimit(operation, subject);
    }

    public async Task EnsureUserAllowedAsync(long userId, CancellationToken cancellationToken = default)
    {
        var exists = await KeyExistsAsync(UserLockKey(userId), cancellationToken);
        if (exists)
            ThrowLockout();
    }

    public async Task EnsureLoginAllowedAsync(string login, string? trustedIpAddress, CancellationToken cancellationToken = default)
    {
        var exists = await KeyExistsAsync(LoginLockKey(login, trustedIpAddress), cancellationToken);
        if (exists)
            ThrowLockout();
    }

    public async Task<IdentityFailureResult> RegisterLoginFailureAsync(
        string login,
        string? trustedIpAddress,
        long? userId,
        CancellationToken cancellationToken = default)
    {
        var failureWindowSeconds = MinutesToSeconds(_options.FailureWindowMinutes, 15);
        var lockoutSeconds = MinutesToSeconds(_options.LockoutMinutes, 15);
        var limit = Positive(_options.FailureLimit, 5);

        int result;
        if (userId.HasValue)
        {
            result = await EvaluateAsync(
                LoginFailureScript,
                [
                    LoginFailureKey(login, trustedIpAddress),
                    UserFailureKey(userId.Value),
                    LoginLockKey(login, trustedIpAddress),
                    UserLockKey(userId.Value)
                ],
                [failureWindowSeconds, limit, lockoutSeconds],
                cancellationToken);
        }
        else
        {
            result = await EvaluateAsync(
                LoginFailureWithoutUserScript,
                [
                    LoginFailureKey(login, trustedIpAddress),
                    LoginLockKey(login, trustedIpAddress)
                ],
                [failureWindowSeconds, limit, lockoutSeconds],
                cancellationToken);
        }

        var failure = ToFailureResult(result, limit);
        if (failure.NewlyLocked)
            _metrics.Increment("identity_lockouts");

        return failure;
    }

    public Task ClearLoginFailuresAsync(
        string login,
        string? trustedIpAddress,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return DeleteKeysAsync(
            cancellationToken,
            LoginFailureKey(login, trustedIpAddress),
            UserFailureKey(userId));
    }

    public async Task EnsureCodeAllowedAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        CancellationToken cancellationToken = default)
    {
        if (await KeyExistsAsync(CodeLockKey(codeKind, codeId), cancellationToken))
            ThrowLockout();
    }

    public async Task<IdentityFailureResult> RegisterCodeFailureAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var failureTtl = PositiveSeconds(
            Math.Min(
                (expiresAt - DateTime.UtcNow).TotalSeconds,
                MinutesToSeconds(_options.FailureWindowMinutes, 15)));
        var lockoutSeconds = PositiveSeconds(
            Math.Min(
                (expiresAt - DateTime.UtcNow).TotalSeconds,
                MinutesToSeconds(_options.LockoutMinutes, 15)));
        var limit = Positive(_options.CodeAttemptLimit, 5);

        var result = await EvaluateAsync(
            FailureScript,
            [CodeFailureKey(codeKind, codeId), CodeLockKey(codeKind, codeId)],
            [failureTtl, limit, lockoutSeconds],
            cancellationToken);

        var failure = ToFailureResult(result, limit);
        if (failure.NewlyLocked)
        {
            _metrics.Increment("identity_code_invalidated");
            _metrics.Increment("identity_lockouts");
        }

        return failure;
    }

    public Task ClearCodeFailuresAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        CancellationToken cancellationToken = default)
    {
        return DeleteKeysAsync(
            cancellationToken,
            CodeFailureKey(codeKind, codeId));
    }

    public async Task EnsureOtpOperationAllowedAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (await KeyExistsAsync(OtpLockKey(operation, userId), cancellationToken))
            ThrowLockout();
    }

    public async Task<IdentityFailureResult> RegisterOtpFailureAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var limit = Positive(_options.OtpAttemptLimit, 5);
        var result = await EvaluateAsync(
            FailureScript,
            [OtpFailureKey(operation, userId), OtpLockKey(operation, userId)],
            [MinutesToSeconds(_options.FailureWindowMinutes, 15), limit, MinutesToSeconds(_options.LockoutMinutes, 15)],
            cancellationToken);

        var failure = ToFailureResult(result, limit);
        if (failure.NewlyLocked)
            _metrics.Increment("identity_lockouts");

        return failure;
    }

    public Task ClearOtpFailuresAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return DeleteKeysAsync(
            cancellationToken,
            OtpFailureKey(operation, userId));
    }

    public async Task DelayAfterFailureAsync(int attempts, CancellationToken cancellationToken = default)
    {
        if (attempts <= 0)
            return;

        var delay = Positive(_options.BackoffBaseMilliseconds, 250);
        var maxDelay = Positive(_options.BackoffMaxMilliseconds, 2000);

        for (var i = 1; i < attempts; i++)
            delay = Math.Min(maxDelay, delay > maxDelay / 2 ? maxDelay : delay * 2);

        await Task.Delay(Math.Min(delay, maxDelay), cancellationToken);
    }

    private async Task<int> IncrementAsync(
        RedisKey key,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var result = await EvaluateAsync(
            IncrementScript,
            [key],
            [PositiveSeconds(ttl.TotalSeconds)],
            cancellationToken);

        return result;
    }

    private async Task<bool> KeyExistsAsync(RedisKey key, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _redis.GetDatabase().KeyExistsAsync(key);
        }
        catch (RedisException ex)
        {
            throw ProtectionUnavailable(ex);
        }
    }

    private async Task<int> EvaluateAsync(
        string script,
        RedisKey[] keys,
        RedisValue[] values,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _redis.GetDatabase().ScriptEvaluateAsync(script, keys, values);
            return int.Parse(result.ToString(), CultureInfo.InvariantCulture);
        }
        catch (RedisException ex)
        {
            throw ProtectionUnavailable(ex);
        }
    }

    private async Task DeleteKeysAsync(CancellationToken cancellationToken, params RedisKey[] keys)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _redis.GetDatabase().KeyDeleteAsync(keys);
        }
        catch (RedisException ex)
        {
            throw ProtectionUnavailable(ex);
        }
    }

    private IdentityProtectionUnavailableException ProtectionUnavailable(Exception exception)
    {
        _metrics.Increment("identity_protection_unavailable");
        _logger.LogError(exception, "Redis недоступен для защиты Identity");
        return new IdentityProtectionUnavailableException();
    }

    private void ThrowRateLimit(IdentityAbuseOperation operation, string? subject)
    {
        _metrics.Increment("identity_rate_limited");
        _logger.LogWarning("Превышен лимит Identity для операции {Operation}, субъект: {Subject}", operation, subject);
        throw new IdentityRateLimitExceededException();
    }

    private static void ThrowLockout()
    {
        throw new IdentityLockoutException();
    }

    private static IdentityFailureResult ToFailureResult(int result, int limit)
    {
        if (result == -1)
            return new IdentityFailureResult(limit, true);

        if (result == -2)
            return new IdentityFailureResult(limit, true, true);

        return new IdentityFailureResult(Math.Max(1, result), result >= limit);
    }

    private static RedisKey RequestKey(string? ipAddress) =>
        $"{KeyPrefix}:requests:{Hash(ipAddress)}";

    private static RedisKey SubjectRequestKey(string subject) =>
        $"{KeyPrefix}:subjects:{Hash(subject)}";

    private static RedisKey LoginFailureKey(string login, string? ipAddress) =>
        $"{KeyPrefix}:login-failures:{Hash(login)}:{Hash(ipAddress)}";

    private static RedisKey LoginLockKey(string login, string? ipAddress) =>
        $"{KeyPrefix}:login-lock:{Hash(login)}:{Hash(ipAddress)}";

    private static RedisKey UserFailureKey(long userId) =>
        $"{KeyPrefix}:user-failures:{userId.ToString(CultureInfo.InvariantCulture)}";

    private static RedisKey UserLockKey(long userId) =>
        $"{KeyPrefix}:user-lock:{userId.ToString(CultureInfo.InvariantCulture)}";

    private static RedisKey CodeFailureKey(IdentityCodeKind codeKind, Guid codeId) =>
        $"{KeyPrefix}:code-failures:{codeKind}:{codeId:N}";

    private static RedisKey CodeLockKey(IdentityCodeKind codeKind, Guid codeId) =>
        $"{KeyPrefix}:code-lock:{codeKind}:{codeId:N}";

    private static RedisKey OtpFailureKey(IdentityOtpOperation operation, long userId) =>
        $"{KeyPrefix}:otp-failures:{operation}:{userId.ToString(CultureInfo.InvariantCulture)}";

    private static RedisKey OtpLockKey(IdentityOtpOperation operation, long userId) =>
        $"{KeyPrefix}:otp-lock:{operation}:{userId.ToString(CultureInfo.InvariantCulture)}";

    private static string Hash(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;

    private static int MinutesToSeconds(int minutes, int fallbackMinutes) =>
        checked(Positive(minutes, fallbackMinutes) * 60);

    private static int PositiveSeconds(double seconds) =>
        Math.Max(1, (int)Math.Ceiling(seconds));
}
