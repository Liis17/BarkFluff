using BarkFluff.GrpcServer.Metrics;

namespace BarkFluff.GrpcServer.Tests;

public class MetricsCollectorTests
{
    [Fact]
    public void SnapshotAndResetDetailed_SeparatesCounterDeltasFromGauges()
    {
        var collector = new MetricsCollector();
        collector.Add("messages_sent", 3);
        collector.Set("online_users_count", 17);

        var snapshot = collector.SnapshotAndResetDetailed(out var hadActivity);

        hadActivity.Should().BeTrue();
        snapshot.Counters.Should().ContainSingle();
        snapshot.Counters["messages_sent"].Should().Be(3);
        snapshot.Gauges.Should().ContainSingle();
        snapshot.Gauges["online_users_count"].Should().Be(17);

        var next = collector.SnapshotAndResetDetailed(out var nextHadActivity);
        nextHadActivity.Should().BeFalse();
        next.Counters.Should().BeEmpty();
        next.Gauges.Should().ContainSingle();
        next.Gauges["online_users_count"].Should().Be(17);
    }
}
