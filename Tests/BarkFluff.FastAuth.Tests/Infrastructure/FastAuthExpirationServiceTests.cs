using System.Collections.Concurrent;
using System.Reflection;
using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Tests.Infrastructure;

public class FastAuthExpirationServiceTests
{
    private readonly FastAuthSessionsManager _manager = new();
    private readonly MetricsCollector _metrics = new();

    private FastAuthExpirationService CreateService()
    {
        var logger = TestHelper.CreateLogger<FastAuthExpirationService>();
        return new FastAuthExpirationService(_manager, _metrics, logger);
    }

    private FastAuthSession AddExpiredSession()
    {
        var session = new FastAuthSession
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow - TimeSpan.FromMinutes(10),
            ExpiresAt = DateTime.UtcNow - TimeSpan.FromMinutes(5),
            DeviceName = "D",
            OperationSystem = "OS",
            AppName = "A",
            AppVersion = "V",
            IpAddress = "IP"
        };

        var field = typeof(FastAuthSessionsManager)
            .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, FastAuthSession>)field.GetValue(_manager)!;
        dict[session.Id] = session;

        return session;
    }

    #region Expiration

    [Fact]
    public async Task ExecuteAsync_ExpiredSession_MarksAsExpired()
    {
        var session = AddExpiredSession();
        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        session.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredSession_IncrementsExpiredMetric()
    {
        AddExpiredSession();
        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        var snapshot = _metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_expired");
        snapshot["sessions_expired"].Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_NonExpiredSession_DoesNotMarkAsExpired()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        session.Status.Should().Be(FastAuthStatus.Pending);
    }

    [Fact]
    public async Task ExecuteAsync_ScannedExpiredSession_MarksAsExpired()
    {
        var session = AddExpiredSession();
        session.TryScan(userId: 42);
        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        session.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyFinalSession_DoesNotExpire()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        session.Status.Should().Be(FastAuthStatus.Rejected);
    }

    #endregion

    #region Retention Cleanup

    [Fact]
    public async Task ExecuteAsync_OldFinalizedSession_IncrementsRemovedMetric()
    {
        var session = AddExpiredSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "token"
        });

        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(32));
        cts.Cancel();
        await service.StopAsync(cts.Token);

        var snapshot = _metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_removed");
    }

    [Fact]
    public async Task ExecuteAsync_RecentFinalizedSession_NotRemoved()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "token"
        });

        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        _manager.TryGet(session.Id).Should().NotBeNull();
    }

    #endregion

    #region Multiple Sessions

    [Fact]
    public async Task ExecuteAsync_MultipleExpiredSessions_ExpiresAll()
    {
        var s1 = AddExpiredSession();
        var s2 = AddExpiredSession();
        var s3 = AddExpiredSession();

        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        s1.Status.Should().Be(FastAuthStatus.Expired);
        s2.Status.Should().Be(FastAuthStatus.Expired);
        s3.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public async Task ExecuteAsync_MixedSessions_OnlyExpiresExpired()
    {
        var expired = AddExpiredSession();
        var active = _manager.Create("D", "OS", "A", "V", "IP");

        var cts = new CancellationTokenSource();
        var service = CreateService();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(cts.Token);

        expired.Status.Should().Be(FastAuthStatus.Expired);
        active.Status.Should().Be(FastAuthStatus.Pending);
    }

    #endregion
}
