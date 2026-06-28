using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Tests.Services;

public class OnlineStatusStorageTests
{
    private readonly OnlineStatusStorage _storage = new();

    [Fact]
    public void UpdateStatus_NewUser_ReturnsTrue()
    {
        var result = _storage.UpdateStatus(1);
        result.Should().BeTrue();
    }

    [Fact]
    public void UpdateStatus_NewUser_SetsStatusOnline()
    {
        _storage.UpdateStatus(1);
        var status = _storage.GetStatus(1);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
        status.UserId.Should().Be(1);
    }

    [Fact]
    public void UpdateStatus_NewUser_SetsLastSeenToUtcNow()
    {
        var before = DateTime.UtcNow;
        _storage.UpdateStatus(1);
        var after = DateTime.UtcNow;
        var status = _storage.GetStatus(1);
        status!.LastSeen.Should().BeOnOrAfter(before);
        status.LastSeen.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void UpdateStatus_AlreadyOnline_ReturnsFalse()
    {
        _storage.UpdateStatus(1);
        var result = _storage.UpdateStatus(1);
        result.Should().BeFalse();
    }

    [Fact]
    public void UpdateStatus_AlreadyOnline_UpdatesLastSeen()
    {
        _storage.UpdateStatus(1);
        var firstSeen = _storage.GetStatus(1)!.LastSeen;
        Thread.Sleep(10);
        _storage.UpdateStatus(1);
        var updated = _storage.GetStatus(1)!;
        updated.LastSeen.Should().BeAfter(firstSeen);
    }

    [Fact]
    public void UpdateStatus_FromOffline_ReturnsTrue()
    {
        _storage.UpdateStatus(1);
        _storage.SetOffline(1);
        var result = _storage.UpdateStatus(1);
        result.Should().BeTrue();
    }

    [Fact]
    public void UpdateStatus_FromOffline_SetsOnline()
    {
        _storage.UpdateStatus(1);
        _storage.SetOffline(1);
        _storage.UpdateStatus(1);
        var status = _storage.GetStatus(1);
        status!.Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public void SetOffline_OnlineUser_ReturnsTrue()
    {
        _storage.UpdateStatus(1);
        var result = _storage.SetOffline(1);
        result.Should().BeTrue();
    }

    [Fact]
    public void SetOffline_OnlineUser_SetsStatusOffline()
    {
        _storage.UpdateStatus(1);
        _storage.SetOffline(1);
        var status = _storage.GetStatus(1);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public void SetOffline_AlreadyOffline_ReturnsFalse()
    {
        _storage.UpdateStatus(1);
        _storage.SetOffline(1);
        var result = _storage.SetOffline(1);
        result.Should().BeFalse();
    }

    [Fact]
    public void SetOffline_UnknownUser_ReturnsFalse()
    {
        var result = _storage.SetOffline(999);
        result.Should().BeFalse();
    }

    [Fact]
    public void SetOffline_UnknownUser_CreatesOfflineRecord()
    {
        _storage.SetOffline(999);
        var status = _storage.GetStatus(999);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public void GetStatus_UnknownUser_ReturnsNull()
    {
        var status = _storage.GetStatus(42);
        status.Should().BeNull();
    }

    [Fact]
    public void GetAllStatuses_Empty_ReturnsEmptyCollection()
    {
        var statuses = _storage.GetAllStatuses();
        statuses.Should().BeEmpty();
    }

    [Fact]
    public void GetAllStatuses_ReturnsAllTrackedUsers()
    {
        _storage.UpdateStatus(1);
        _storage.UpdateStatus(2);
        _storage.UpdateStatus(3);
        var statuses = _storage.GetAllStatuses();
        statuses.Should().HaveCount(3);
    }

    [Fact]
    public void GetAllStatuses_ReturnsImmutableSnapshots()
    {
        _storage.UpdateStatus(1);
        var snapshot = _storage.GetAllStatuses();
        _storage.SetOffline(1);
        snapshot.First().Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public void GetOnlineUsersOlderThan_NoOnlineUsers_ReturnsEmpty()
    {
        var result = _storage.GetOnlineUsersOlderThan(TimeSpan.FromSeconds(5));
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetOnlineUsersOlderThan_RecentOnlineUser_ReturnsEmpty()
    {
        _storage.UpdateStatus(1);
        var result = _storage.GetOnlineUsersOlderThan(TimeSpan.FromSeconds(5));
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetOnlineUsersOlderThan_OldOnlineUser_ReturnsUser()
    {
        _storage.UpdateStatus(2);
        var result = _storage.GetOnlineUsersOlderThan(TimeSpan.Zero);
        result.Should().Contain(2);
    }

    [Fact]
    public void GetOnlineUsersOlderThan_OfflineUserNotReturned()
    {
        _storage.UpdateStatus(1);
        _storage.SetOffline(1);
        var result = _storage.GetOnlineUsersOlderThan(TimeSpan.Zero);
        result.Should().NotContain(1);
    }

    [Fact]
    public void GetOnlineCount_NoUsers_ReturnsZero()
    {
        _storage.GetOnlineCount().Should().Be(0);
    }

    [Fact]
    public void GetOnlineCount_OnlyOnlineUsers()
    {
        _storage.UpdateStatus(1);
        _storage.UpdateStatus(2);
        _storage.UpdateStatus(3);
        _storage.SetOffline(2);
        _storage.GetOnlineCount().Should().Be(2);
    }

    [Fact]
    public void GetTotalCount_NoUsers_ReturnsZero()
    {
        _storage.GetTotalCount().Should().Be(0);
    }

    [Fact]
    public void GetTotalCount_AllUsers()
    {
        _storage.UpdateStatus(1);
        _storage.UpdateStatus(2);
        _storage.SetOffline(1);
        _storage.GetTotalCount().Should().Be(2);
    }

    [Fact]
    public async Task UpdateStatus_ConcurrentUpdates_ThreadSafe()
    {
        const int userId = 1;
        const int concurrency = 100;
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => _storage.UpdateStatus(userId)));
        var results = await Task.WhenAll(tasks);

        results.Should().ContainSingle(r => r == true);
        results.Count(r => r == false).Should().Be(concurrency - 1);
        var status = _storage.GetStatus(userId);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task SetOffline_ConcurrentOffline_ThreadSafe()
    {
        const int userId = 1;
        _storage.UpdateStatus(userId);
        const int concurrency = 100;
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => _storage.SetOffline(userId)));
        var results = await Task.WhenAll(tasks);

        results.Should().ContainSingle(r => r == true);
        var status = _storage.GetStatus(userId);
        status!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public async Task UpdateStatus_ConcurrentMultipleUsers_NoCrosstalk()
    {
        const int userCount = 50;
        var userIds = Enumerable.Range(1, userCount).ToList();
        var tasks = userIds.SelectMany(id => Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => _storage.UpdateStatus(id))));
        await Task.WhenAll(tasks);

        _storage.GetTotalCount().Should().Be(userCount);
        foreach (var id in userIds)
        {
            var status = _storage.GetStatus(id);
            status.Should().NotBeNull();
            status!.Status.Should().Be(DomainStatusTypeId.Online);
        }
    }

    [Fact]
    public void UpdateStatus_IdempotentHeartbeat()
    {
        _storage.UpdateStatus(1);
        _storage.UpdateStatus(1);
        _storage.UpdateStatus(1);
        _storage.GetOnlineCount().Should().Be(1);
        _storage.GetTotalCount().Should().Be(1);
    }
}
