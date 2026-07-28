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

    [Fact]
    public void ImmediateMetric_IsPublishedWithoutWaitingForBufferedFlush()
    {
        var collector = new MetricsCollector(MetricsExportProfile.ImmediateByDefault());

        collector.Increment("auth_login_success");

        collector.ImmediateSnapshots.TryRead(out var snapshot).Should().BeTrue();
        snapshot.Counters.Should().ContainSingle().Which.Value.Should().Be(1);
        collector.TakeBufferedSnapshot().Counters.Should().BeEmpty();
    }

    [Fact]
    public void BufferedMetric_IsSummedAndIdleSnapshotIsEmpty()
    {
        var collector = new MetricsCollector(MetricsExportProfile.ImmediateByDefault("messages_sent"));

        collector.Add("messages_sent", 2);
        collector.Add("messages_sent", 3);

        var snapshot = collector.TakeBufferedSnapshot();
        snapshot.Counters["messages_sent"].Should().Be(5);
        collector.TakeBufferedSnapshot().Counters.Should().BeEmpty();
        collector.TakeBufferedSnapshot().Gauges.Should().BeEmpty();
    }

    [Fact]
    public void Gauge_IsExportedOnlyWhenItChanges()
    {
        var collector = new MetricsCollector(MetricsExportProfile.ImmediateByDefault());

        collector.Set("online_users_count", 10);
        collector.TakeBufferedSnapshot().Gauges["online_users_count"].Should().Be(10);
        collector.Set("online_users_count", 10);

        collector.TakeBufferedSnapshot().Gauges.Should().BeEmpty();
    }
}
