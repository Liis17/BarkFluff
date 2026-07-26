using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Services;

/// <summary>
/// Coalescing и адресация входящих presence-подписок (этап 4.3).
/// </summary>
public class IncomingPresenceCoalescingTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static IncomingPresenceSubscription CreateSubscription(params long[] userIds)
        => new("node-b.test", userIds.ToDictionary(id => id, _ => Guid.NewGuid()));

    [Fact]
    public void TakeDue_ManyChangesInWindow_YieldOneSend()
    {
        // N изменений подряд стоят одной отправки: состояние перечитывается у Onliner
        // в момент отправки, поэтому «последнее» уходит автоматически.
        var subscription = CreateSubscription(10);
        var now = DateTime.UtcNow;

        subscription.MarkDirty(10);
        subscription.TakeDue(now, Window).Should().BeEquivalentTo([10L]);

        for (var i = 0; i < 5; i++)
        {
            subscription.MarkDirty(10);
        }

        subscription.TakeDue(now.AddSeconds(1), Window).Should().BeEmpty();
        subscription.TakeDue(now.AddSeconds(4), Window).Should().BeEmpty();

        // За пределами окна накопленное изменение уходит одним событием.
        subscription.TakeDue(now.AddSeconds(6), Window).Should().BeEquivalentTo([10L]);
    }

    [Fact]
    public void TakeDue_WithoutChanges_SendsNothing()
    {
        var subscription = CreateSubscription(10);

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEmpty();
    }

    [Fact]
    public void MarkDirty_UnwatchedUser_IsIgnored()
    {
        // Подписка видит только разрешённых пользователей — чужие изменения в неё не попадают.
        var subscription = CreateSubscription(10);

        subscription.MarkDirty(999);

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEmpty();
    }

    [Fact]
    public void MarkAllDirty_QueuesEveryWatchedUser()
    {
        // Начальный снимок и периодический ресинк.
        var subscription = CreateSubscription(10, 20, 30);

        subscription.MarkAllDirty();

        subscription.TakeDue(DateTime.UtcNow, Window).Should().BeEquivalentTo([10L, 20L, 30L]);
    }

    [Fact]
    public void Registry_MarksOnlySubscriptionsWatchingUser()
    {
        var registry = new IncomingPresenceRegistry();
        var watching = registry.Add("node-b.test", new Dictionary<long, Guid> { [10] = Guid.NewGuid() });
        var notWatching = registry.Add("node-c.test", new Dictionary<long, Guid> { [20] = Guid.NewGuid() });

        registry.MarkStatusChanged(10);

        watching.TakeDue(DateTime.UtcNow, Window).Should().BeEquivalentTo([10L]);
        notWatching.TakeDue(DateTime.UtcNow, Window).Should().BeEmpty();
    }

    [Fact]
    public void Registry_RemoveDropsSubscription()
    {
        var registry = new IncomingPresenceRegistry();
        var subscription = registry.Add("node-b.test", new Dictionary<long, Guid> { [10] = Guid.NewGuid() });

        registry.Count.Should().Be(1);
        registry.WatchedTotal.Should().Be(1);

        registry.Remove(subscription.Id);

        registry.Count.Should().Be(0);
        registry.WatchedTotal.Should().Be(0);
    }
}
