using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Services;

public class OnlineStatusSubscriptionsManagerTests
{
    private readonly OnlineStatusSubscriptionsManager _manager = new();

    private Mock<IServerStreamWriter<ProtoUserOnlineStatus>> CreateStream()
    {
        return new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
    }

    [Fact]
    public void RegisterSubscription_ReturnsConnectionId()
    {
        var stream = CreateStream();
        var connectionId = _manager.RegisterSubscription(1, [10, 20], stream.Object);
        connectionId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void RegisterSubscription_MultipleConnectionsForSameUser()
    {
        var stream1 = CreateStream();
        var stream2 = CreateStream();
        var id1 = _manager.RegisterSubscription(1, [10], stream1.Object);
        var id2 = _manager.RegisterSubscription(1, [20], stream2.Object);
        id1.Should().NotBe(id2);
        _manager.GetActiveSubscriptionsCount().Should().Be(2);
    }

    [Fact]
    public void RegisterSubscription_SameUserDifferentConnections()
    {
        var stream1 = CreateStream();
        var stream2 = CreateStream();
        _manager.RegisterSubscription(1, [10, 20], stream1.Object);
        _manager.RegisterSubscription(1, [30], stream2.Object);
        _manager.GetStreamsTrackingUser(10).Should().HaveCount(1);
        _manager.GetStreamsTrackingUser(30).Should().HaveCount(1);
    }

    [Fact]
    public void RemoveSubscription_RemovesFromForwardIndex()
    {
        var stream = CreateStream();
        var connectionId = _manager.RegisterSubscription(1, [10], stream.Object);
        _manager.RemoveSubscription(1, connectionId);
        _manager.GetActiveSubscriptionsCount().Should().Be(0);
    }

    [Fact]
    public void RemoveSubscription_RemovesFromReverseIndex()
    {
        var stream = CreateStream();
        var connectionId = _manager.RegisterSubscription(1, [10], stream.Object);
        _manager.RemoveSubscription(1, connectionId);
        _manager.GetStreamsTrackingUser(10).Should().BeEmpty();
    }

    [Fact]
    public void RemoveSubscription_OnlyRemovesTargetConnection()
    {
        var stream1 = CreateStream();
        var stream2 = CreateStream();
        var id1 = _manager.RegisterSubscription(1, [10], stream1.Object);
        _manager.RegisterSubscription(1, [10], stream2.Object);
        _manager.RemoveSubscription(1, id1);
        _manager.GetActiveSubscriptionsCount().Should().Be(1);
        _manager.GetStreamsTrackingUser(10).Should().HaveCount(1);
    }

    [Fact]
    public void RemoveSubscription_CleansEmptyUserEntry()
    {
        var stream = CreateStream();
        var connectionId = _manager.RegisterSubscription(1, [10], stream.Object);
        _manager.RemoveSubscription(1, connectionId);
        _manager.GetActiveSubscriptionsCount().Should().Be(0);
    }

    [Fact]
    public void RemoveSubscription_NonExistentUser_DoesNothing()
    {
        var act = () => _manager.RemoveSubscription(999, Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveSubscription_NonExistentConnection_DoesNothing()
    {
        var stream = CreateStream();
        _manager.RegisterSubscription(1, [10], stream.Object);
        _manager.RemoveSubscription(1, Guid.NewGuid());
        _manager.GetActiveSubscriptionsCount().Should().Be(1);
    }

    [Fact]
    public void GetStreamsTrackingUser_NoSubscriptions_ReturnsEmpty()
    {
        _manager.GetStreamsTrackingUser(42).Should().BeEmpty();
    }

    [Fact]
    public void GetStreamsTrackingUser_MultipleSubscribersTrackingSameUser()
    {
        var stream1 = CreateStream();
        var stream2 = CreateStream();
        _manager.RegisterSubscription(1, [100], stream1.Object);
        _manager.RegisterSubscription(2, [100], stream2.Object);
        _manager.GetStreamsTrackingUser(100).Should().HaveCount(2);
    }

    [Fact]
    public void GetStreamsTrackingUser_SubscriberNotTrackingUser_ReturnsEmpty()
    {
        var stream = CreateStream();
        _manager.RegisterSubscription(1, [10, 20], stream.Object);
        _manager.GetStreamsTrackingUser(999).Should().BeEmpty();
    }

    [Fact]
    public void GetStreamsTrackingUser_TrackedInMultipleConnections()
    {
        var s1 = CreateStream();
        var s2 = CreateStream();
        _manager.RegisterSubscription(1, [50, 60], s1.Object);
        _manager.RegisterSubscription(1, [50], s2.Object);
        _manager.GetStreamsTrackingUser(50).Should().HaveCount(2);
    }

    [Fact]
    public void RegisterSubscription_EmptyTrackedList_RegistersWithNoReverseIndex()
    {
        var stream = CreateStream();
        var connectionId = _manager.RegisterSubscription(1, [], stream.Object);
        connectionId.Should().NotBe(Guid.Empty);
        _manager.GetActiveSubscriptionsCount().Should().Be(1);
        _manager.GetTrackedUniqueUsersCount().Should().Be(0);
    }

    [Fact]
    public void RegisterSubscription_DuplicateTrackedIds_DeduplicatesInReverseIndex()
    {
        var stream = CreateStream();
        _manager.RegisterSubscription(1, [10, 10, 10], stream.Object);
        _manager.GetTrackedUniqueUsersCount().Should().Be(1);
        _manager.GetStreamsTrackingUser(10).Should().HaveCount(1);
    }

    [Fact]
    public void UpdateAllSubscriptions_NoActiveSubscriptions_ReturnsZero()
    {
        var result = _manager.UpdateAllSubscriptions(1, [10, 20]);
        result.Should().Be(0);
    }

    [Fact]
    public void UpdateAllSubscriptions_UpdatesSingleSubscription()
    {
        var stream = CreateStream();
        _manager.RegisterSubscription(1, [10, 20], stream.Object);
        var updated = _manager.UpdateAllSubscriptions(1, [30, 40]);
        updated.Should().Be(1);
        _manager.GetStreamsTrackingUser(10).Should().BeEmpty();
        _manager.GetStreamsTrackingUser(20).Should().BeEmpty();
        _manager.GetStreamsTrackingUser(30).Should().HaveCount(1);
        _manager.GetStreamsTrackingUser(40).Should().HaveCount(1);
    }

    [Fact]
    public void UpdateAllSubscriptions_UpdatesMultipleConnections()
    {
        var s1 = CreateStream();
        var s2 = CreateStream();
        _manager.RegisterSubscription(1, [10], s1.Object);
        _manager.RegisterSubscription(1, [20], s2.Object);
        var updated = _manager.UpdateAllSubscriptions(1, [30]);
        updated.Should().Be(2);
        _manager.GetStreamsTrackingUser(30).Should().HaveCount(2);
        _manager.GetStreamsTrackingUser(10).Should().BeEmpty();
        _manager.GetStreamsTrackingUser(20).Should().BeEmpty();
    }

    [Fact]
    public void UpdateAllSubscriptions_PreservesStreamReferences()
    {
        var s1 = CreateStream();
        _manager.RegisterSubscription(1, [10], s1.Object);
        _manager.UpdateAllSubscriptions(1, [20]);
        var streams = _manager.GetStreamsTrackingUser(20);
        streams.Should().ContainSingle().Which.Should().Be(s1.Object);
    }

    [Fact]
    public void GetActiveSubscriptionsCount_NoSubscriptions_ReturnsZero()
    {
        _manager.GetActiveSubscriptionsCount().Should().Be(0);
    }

    [Fact]
    public void GetActiveSubscriptionsCount_MultipleUsers()
    {
        var s1 = CreateStream();
        var s2 = CreateStream();
        var s3 = CreateStream();
        _manager.RegisterSubscription(1, [10], s1.Object);
        _manager.RegisterSubscription(2, [20], s2.Object);
        _manager.RegisterSubscription(2, [30], s3.Object);
        _manager.GetActiveSubscriptionsCount().Should().Be(3);
    }

    [Fact]
    public void GetTrackedUniqueUsersCount_NoSubscriptions_ReturnsZero()
    {
        _manager.GetTrackedUniqueUsersCount().Should().Be(0);
    }

    [Fact]
    public void GetTrackedUniqueUsersCount_CountsUniqueTrackedUsers()
    {
        var s1 = CreateStream();
        var s2 = CreateStream();
        _manager.RegisterSubscription(1, [10, 20], s1.Object);
        _manager.RegisterSubscription(2, [20, 30], s2.Object);
        _manager.GetTrackedUniqueUsersCount().Should().Be(3);
    }

    [Fact]
    public async Task RegisterSubscription_ConcurrentRegistrations_ThreadSafe()
    {
        const int count = 100;
        var streamMocks = Enumerable.Range(0, count).Select(_ => CreateStream()).ToList();
        var connectionIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var tasks = Enumerable.Range(0, count).Select(i =>
            Task.Run(() =>
            {
                var id = _manager.RegisterSubscription(i % 10, [100 + i], streamMocks[i].Object);
                connectionIds.Add(id);
            }));
        await Task.WhenAll(tasks);
        connectionIds.Should().HaveCount(count);
        _manager.GetActiveSubscriptionsCount().Should().Be(count);
    }
}
