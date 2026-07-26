using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;

namespace BarkFluff.Federation.Tests.Services;

public class PresenceInterestRegistryTests
{
    private static PresenceInterestRegistry Create(TestTimeProvider time, int ttlSeconds = 60)
        => new(
            TestHelpers.CreatePresenceOptions(new Dictionary<string, string?>
            {
                ["Federation:PresenceInterestTtlSeconds"] = ttlSeconds.ToString(),
            }),
            time);

    [Fact]
    public void GetUnion_MergesSetsOfSeveralInstances()
    {
        // Стримы подписчиков живут на разных инстансах Onliner — union обязателен,
        // иначе нода следила бы только за интересом последнего отчитавшегося.
        var time = new TestTimeProvider();
        var registry = Create(time);

        var shared = Guid.NewGuid();
        var onlyFirst = Guid.NewGuid();
        var onlySecond = Guid.NewGuid();

        registry.Set("instance-1", [shared, onlyFirst]);
        registry.Set("instance-2", [shared, onlySecond]);

        registry.GetUnion().Should().BeEquivalentTo([shared, onlyFirst, onlySecond]);
        registry.LiveInstanceCount.Should().Be(2);
    }

    [Fact]
    public void Set_ReplacesInstanceSetEntirely()
    {
        // Приходит ПОЛНЫЙ набор, а не дельта: старые uuid обязаны исчезнуть.
        var time = new TestTimeProvider();
        var registry = Create(time);

        var oldUuid = Guid.NewGuid();
        var newUuid = Guid.NewGuid();

        registry.Set("instance-1", [oldUuid]);
        registry.Set("instance-1", [newUuid]);

        registry.GetUnion().Should().BeEquivalentTo([newUuid]);
    }

    [Fact]
    public void GetUnion_DropsExpiredInstances()
    {
        // Рестарт инстанса Onliner самолечится через TTL — отдельная чистка не нужна.
        var time = new TestTimeProvider();
        var registry = Create(time, ttlSeconds: 60);

        var stale = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        registry.Set("dead-instance", [stale]);
        time.Advance(TimeSpan.FromSeconds(45));
        registry.Set("live-instance", [fresh]);
        time.Advance(TimeSpan.FromSeconds(30));

        registry.GetUnion().Should().BeEquivalentTo([fresh]);
        registry.LiveInstanceCount.Should().Be(1);
    }

    [Fact]
    public void Set_EmptySetIsValidState()
    {
        // «Инстанс жив, подписчиков нет» — сигнал закрыть S2S-подписку, а не отсутствие записи.
        var time = new TestTimeProvider();
        var registry = Create(time);

        registry.Set("instance-1", [Guid.NewGuid()]);
        registry.Set("instance-1", []);

        registry.GetUnion().Should().BeEmpty();
        registry.LiveInstanceCount.Should().Be(1);
    }
}
