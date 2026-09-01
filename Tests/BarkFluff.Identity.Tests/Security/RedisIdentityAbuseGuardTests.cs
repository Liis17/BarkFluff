using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Security;
using BarkFluff.Identity.Settings;
using BarkFluff.Shared.Exceptions.Identity;

using Microsoft.Extensions.Logging;

using Moq;

using StackExchange.Redis;

using Xunit;

namespace BarkFluff.Identity.Tests.Security;

public class RedisIdentityAbuseGuardTests
{
    [Fact]
    public async Task RequestLimit_UsesAtomicScriptAndRejectsAfterConfiguredLimit()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        RedisValue[]? capturedValues = null;

        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) => capturedValues = values)
            .ReturnsAsync(RedisResult.Create((RedisValue)"61"));

        var guard = CreateGuard(redis, new IdentitySecurityOptions { HighRiskRequestsPerMinute = 60 });

        await Assert.ThrowsAsync<IdentityRateLimitExceededException>(() =>
            guard.EnsureRequestAllowedAsync(IdentityAbuseOperation.Auth, "203.0.113.4", null, false));

        Assert.Equal("60", capturedValues![0].ToString());
        database.Verify(x => x.ScriptEvaluateAsync(
            It.Is<string>(script => script.Contains("INCR", StringComparison.Ordinal)),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SubjectLimit_UsesSeparateWindowAndRejectsAfterFifthRequest()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        database
            .SetupSequence(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)"1"))
            .ReturnsAsync(RedisResult.Create((RedisValue)"6"));

        var guard = CreateGuard(redis, new IdentitySecurityOptions
        {
            HighRiskRequestsPerMinute = 100,
            SubjectRequestsPerWindow = 5,
            SubjectWindowMinutes = 15
        });

        await Assert.ThrowsAsync<IdentityRateLimitExceededException>(() => guard.EnsureRequestAllowedAsync(
            IdentityAbuseOperation.CreateAccount,
            "203.0.113.4",
            "alice@example.com",
            true));

        database.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(values => values.Length == 1 && values[0].ToString() == "900"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task SubjectLimit_IsSharedAcrossHighRiskCodeOperations()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        var capturedKeys = new List<RedisKey[]>();

        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, keys, _, _) => capturedKeys.Add(keys))
            .ReturnsAsync(RedisResult.Create((RedisValue)"1"));

        var guard = CreateGuard(redis);
        await guard.EnsureSubjectRequestAllowedAsync(IdentityAbuseOperation.CreateAccount, "alice@example.com");
        await guard.EnsureSubjectRequestAllowedAsync(IdentityAbuseOperation.ResetPassword, "alice@example.com");

        Assert.Equal(2, capturedKeys.Count);
        Assert.Equal(capturedKeys[0][0], capturedKeys[1][0]);
    }

    [Fact]
    public async Task ParallelSubjectRequests_RespectTheAtomicThreshold()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        var count = 0;

        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string _, RedisKey[] _, RedisValue[] _, CommandFlags _) =>
                Task.FromResult(RedisResult.Create((RedisValue)Interlocked.Increment(ref count).ToString())));

        var guard = CreateGuard(redis, new IdentitySecurityOptions
        {
            SubjectRequestsPerWindow = 5,
            SubjectWindowMinutes = 15,
            BackoffBaseMilliseconds = 1,
            BackoffMaxMilliseconds = 1
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            guard.EnsureSubjectRequestAllowedAsync(
                IdentityAbuseOperation.CreateAccount,
                "alice@example.com"))
            .Select(task => task.ContinueWith(
                completed => completed.IsFaulted,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)));

        Assert.Equal(20, count);
        Assert.Equal(15, results.Count(failed => failed));
    }

    [Fact]
    public async Task LoginFailure_ReportsLockoutAndSuccessClearsBothCounters()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)"-2"));

        var guard = CreateGuard(redis, new IdentitySecurityOptions { BackoffBaseMilliseconds = 1, BackoffMaxMilliseconds = 1 });
        var failure = await guard.RegisterLoginFailureAsync("user", "203.0.113.4", 42);

        Assert.True(failure.Locked);
        Assert.True(failure.NewlyLocked);

        database.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<IdentityLockoutException>(() => guard.EnsureUserAllowedAsync(42));

        await guard.ClearLoginFailuresAsync("user", "203.0.113.4", 42);
        database.Verify(x => x.KeyDeleteAsync(It.Is<RedisKey[]>(keys => keys.Length == 2), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task LoginFailureKeysAreSeparatedByLoginAndIp()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        var capturedKeys = new List<RedisKey[]>();

        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, keys, _, _) => capturedKeys.Add(keys))
            .ReturnsAsync(RedisResult.Create((RedisValue)"1"));

        var guard = CreateGuard(redis);
        await guard.RegisterLoginFailureAsync("alice", "203.0.113.4", 1);
        await guard.RegisterLoginFailureAsync("alice", "203.0.113.5", 1);
        await guard.RegisterLoginFailureAsync("bob", "203.0.113.4", 1);

        Assert.Equal(3, capturedKeys.Count);
        Assert.NotEqual(capturedKeys[0][0], capturedKeys[1][0]);
        Assert.NotEqual(capturedKeys[0][0], capturedKeys[2][0]);
        Assert.All(capturedKeys, keys => Assert.All(keys, key => Assert.DoesNotContain("alice", key.ToString())));
    }

    [Fact]
    public async Task CodeFailure_UsesCodeExpiryForTtlAndCanBeCleared()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        RedisValue[]? capturedValues = null;

        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) => capturedValues = values)
            .ReturnsAsync(RedisResult.Create((RedisValue)"-2"));

        var guard = CreateGuard(redis, new IdentitySecurityOptions
        {
            FailureWindowMinutes = 15,
            LockoutMinutes = 15,
            CodeAttemptLimit = 5,
            BackoffBaseMilliseconds = 1,
            BackoffMaxMilliseconds = 1
        });

        var codeId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var failure = await guard.RegisterCodeFailureAsync(
            IdentityCodeKind.Registration,
            codeId,
            expiresAt);

        Assert.True(failure.Locked);
        Assert.True(failure.NewlyLocked);
        Assert.InRange(int.Parse(capturedValues![0].ToString()), 1, 301);
        Assert.InRange(int.Parse(capturedValues[2].ToString()), 1, 301);

        database.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<IdentityLockoutException>(() => guard.EnsureCodeAllowedAsync(
            IdentityCodeKind.Registration,
            codeId));

        await guard.ClearCodeFailuresAsync(IdentityCodeKind.Registration, codeId);
        database.Verify(x => x.KeyDeleteAsync(It.Is<RedisKey[]>(keys => keys.Length == 1), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RedisFailureIsFailClosed()
    {
        var database = new Mock<IDatabase>();
        var redis = CreateRedis(database);
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline"));

        var guard = CreateGuard(redis);

        await Assert.ThrowsAsync<IdentityProtectionUnavailableException>(() =>
            guard.EnsureRequestAllowedAsync(IdentityAbuseOperation.Auth, "203.0.113.4", null, false));
    }

    private static IConnectionMultiplexer CreateRedis(Mock<IDatabase> database)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        return redis.Object;
    }

    private static RedisIdentityAbuseGuard CreateGuard(
        IConnectionMultiplexer redis,
        IdentitySecurityOptions? options = null)
    {
        return new RedisIdentityAbuseGuard(
            redis,
            options ?? new IdentitySecurityOptions { BackoffBaseMilliseconds = 1, BackoffMaxMilliseconds = 1 },
            new MetricsCollector(),
            Mock.Of<ILogger<RedisIdentityAbuseGuard>>());
    }
}
