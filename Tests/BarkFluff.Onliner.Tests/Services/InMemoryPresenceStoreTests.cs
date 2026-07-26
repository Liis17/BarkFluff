using BarkFluff.Onliner.Tests.Fakes;

namespace BarkFluff.Onliner.Tests.Services;

/// <summary>
/// Контракт <c>IPresenceStore</c> на in-memory fake (та же семантика, что у Redis-стора):
/// online = член множества, offline-записей нет, SetOffline (ZREM) возвращает true только для online.
/// </summary>
public class InMemoryPresenceStoreTests
{
    private readonly InMemoryPresenceStore _store = new();

    [Fact]
    public async Task MarkOnline_NewUser_ReturnsTrue()
    {
        (await _store.MarkOnlineAsync(1)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkOnline_NewUser_GetOnlineReturnsOnline()
    {
        await _store.MarkOnlineAsync(1);
        var status = await _store.GetOnlineAsync(1);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
        status.UserId.Should().Be(1);
    }

    [Fact]
    public async Task MarkOnline_AlreadyOnline_ReturnsFalse()
    {
        await _store.MarkOnlineAsync(1);
        (await _store.MarkOnlineAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task MarkOnline_AfterOffline_ReturnsTrue()
    {
        await _store.MarkOnlineAsync(1);
        await _store.SetOfflineAsync(1);
        (await _store.MarkOnlineAsync(1)).Should().BeTrue();
    }

    [Fact]
    public async Task SetOffline_OnlineUser_ReturnsTrue()
    {
        await _store.MarkOnlineAsync(1);
        (await _store.SetOfflineAsync(1)).Should().BeTrue();
    }

    [Fact]
    public async Task SetOffline_OnlineUser_GetOnlineReturnsNull()
    {
        await _store.MarkOnlineAsync(1);
        await _store.SetOfflineAsync(1);
        (await _store.GetOnlineAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task SetOffline_AlreadyOffline_ReturnsFalse()
    {
        await _store.MarkOnlineAsync(1);
        await _store.SetOfflineAsync(1);
        (await _store.SetOfflineAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task SetOffline_UnknownUser_ReturnsFalse()
    {
        (await _store.SetOfflineAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetOnline_UnknownUser_ReturnsNull()
    {
        (await _store.GetOnlineAsync(42)).Should().BeNull();
    }

    [Fact]
    public async Task GetStaleUsers_NoUsers_ReturnsEmpty()
    {
        (await _store.GetStaleUsersAsync(TimeSpan.FromSeconds(5))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetStaleUsers_RecentUser_ReturnsEmpty()
    {
        await _store.MarkOnlineAsync(1);
        (await _store.GetStaleUsersAsync(TimeSpan.FromSeconds(5))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetStaleUsers_OldUser_ReturnsUser()
    {
        await _store.MarkOnlineAsync(2);
        _store.SetLastSeen(2, DateTime.UtcNow.AddSeconds(-10));
        var stale = await _store.GetStaleUsersAsync(TimeSpan.FromSeconds(5));
        stale.Select(s => s.UserId).Should().Contain(2);
    }

    [Fact]
    public async Task GetStaleUsers_OfflineUserNotReturned()
    {
        await _store.MarkOnlineAsync(1);
        await _store.SetOfflineAsync(1);
        var stale = await _store.GetStaleUsersAsync(TimeSpan.Zero);
        stale.Select(s => s.UserId).Should().NotContain(1);
    }

    [Fact]
    public async Task GetOnlineSnapshot_ReturnsOnlineOnly()
    {
        await _store.MarkOnlineAsync(1);
        await _store.MarkOnlineAsync(2);
        await _store.MarkOnlineAsync(3);
        await _store.SetOfflineAsync(2);
        var snapshot = await _store.GetOnlineSnapshotAsync();
        snapshot.Select(s => s.UserId).Should().BeEquivalentTo(new[] { 1L, 3L });
    }

    [Fact]
    public async Task GetOnlineCount_OnlyOnlineUsers()
    {
        await _store.MarkOnlineAsync(1);
        await _store.MarkOnlineAsync(2);
        await _store.MarkOnlineAsync(3);
        await _store.SetOfflineAsync(2);
        (await _store.GetOnlineCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MarkOnline_IdempotentHeartbeat_CountOne()
    {
        await _store.MarkOnlineAsync(1);
        await _store.MarkOnlineAsync(1);
        await _store.MarkOnlineAsync(1);
        (await _store.GetOnlineCountAsync()).Should().Be(1);
    }
}
