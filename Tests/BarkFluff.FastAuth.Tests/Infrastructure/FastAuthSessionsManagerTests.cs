using BarkFluff.FastAuth.Infrastructure;

namespace BarkFluff.FastAuth.Tests.Infrastructure;

public class FastAuthSessionsManagerTests
{
    private readonly FastAuthSessionsManager _manager = new();

    [Fact]
    public void Create_ReturnsSessionWithCorrectProperties()
    {
        var session = _manager.Create("Device", "Android", "BarkFluff", "2.0", "10.0.0.1");

        session.DeviceName.Should().Be("Device");
        session.OperationSystem.Should().Be("Android");
        session.AppName.Should().Be("BarkFluff");
        session.AppVersion.Should().Be("2.0");
        session.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public void Create_SetsGuidId()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        session.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(session.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void Create_SetsCreatedAtToUtcNow()
    {
        var before = DateTime.UtcNow;
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        var after = DateTime.UtcNow;
        session.CreatedAt.Should().BeOnOrAfter(before);
        session.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Create_SetsExpiresAtToCreatedAtPlusTtl()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        session.ExpiresAt.Should().BeCloseTo(
            session.CreatedAt + FastAuthSessionsManager.SessionTtl,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_SetsInitialStatusToPending()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        session.Status.Should().Be(BarkFluff.Proto.FastAuth.FastAuthStatus.Pending);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var s1 = _manager.Create("D", "OS", "A", "V", "IP");
        var s2 = _manager.Create("D", "OS", "A", "V", "IP");
        s1.Id.Should().NotBe(s2.Id);
    }

    [Fact]
    public void TryGet_ExistingSession_ReturnsSession()
    {
        var created = _manager.Create("D", "OS", "A", "V", "IP");
        var found = _manager.TryGet(created.Id);
        found.Should().BeSameAs(created);
    }

    [Fact]
    public void TryGet_NonexistentSession_ReturnsNull()
    {
        var result = _manager.TryGet("nonexistent-id");
        result.Should().BeNull();
    }

    [Fact]
    public void TryGet_EmptyId_ReturnsNull()
    {
        var result = _manager.TryGet("");
        result.Should().BeNull();
    }

    [Fact]
    public void Remove_ExistingSession_ReturnsTrue()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        _manager.Remove(session.Id).Should().BeTrue();
    }

    [Fact]
    public void Remove_ExistingSession_SessionNoLongerAccessible()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        _manager.Remove(session.Id);
        _manager.TryGet(session.Id).Should().BeNull();
    }

    [Fact]
    public void Remove_NonexistentSession_ReturnsFalse()
    {
        _manager.Remove("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void Remove_AlreadyRemoved_ReturnsFalse()
    {
        var session = _manager.Create("D", "OS", "A", "V", "IP");
        _manager.Remove(session.Id);
        _manager.Remove(session.Id).Should().BeFalse();
    }

    [Fact]
    public void Snapshot_Empty_ReturnsEmptyCollection()
    {
        _manager.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Snapshot_ReturnsAllSessions()
    {
        _manager.Create("D1", "OS", "A", "V", "IP");
        _manager.Create("D2", "OS", "A", "V", "IP");
        _manager.Create("D3", "OS", "A", "V", "IP");
        _manager.Snapshot().Should().HaveCount(3);
    }

    [Fact]
    public void Snapshot_AfterRemove_ReturnsRemainingSessions()
    {
        var s1 = _manager.Create("D1", "OS", "A", "V", "IP");
        _manager.Create("D2", "OS", "A", "V", "IP");
        _manager.Remove(s1.Id);
        _manager.Snapshot().Should().HaveCount(1);
    }

    [Fact]
    public void Snapshot_ReturnsImmutableCopy()
    {
        _manager.Create("D1", "OS", "A", "V", "IP");
        var snapshot = _manager.Snapshot();
        _manager.Create("D2", "OS", "A", "V", "IP");
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void SessionTtl_IsFiveMinutes()
    {
        FastAuthSessionsManager.SessionTtl.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void FinalRetention_IsThirtySeconds()
    {
        FastAuthSessionsManager.FinalRetention.Should().Be(TimeSpan.FromSeconds(30));
    }
}
