using System.Text.Json;
using BarkFluff.Status.Catalog;
using BarkFluff.Status.Probing;
using BarkFluff.Status.Reporting;
using BarkFluff.Status.Storage;
using Xunit;

namespace BarkFluff.Status.Tests;

public class StatusReportBuilderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly long TodayStart = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long CurrentSlot = StatusReportBuilder.SlotOf(Noon);

    private static ServiceInput Service(
        string key = "messages",
        bool enabled = true,
        long? firstSlot = null,
        LatestRecord? latest = null,
        Dictionary<string, DayCounts>? days = null) =>
        new(key, key, enabled, firstSlot, latest, days ?? new Dictionary<string, DayCounts>());

    [Fact]
    public void WindowHasNinetyDaysEndingToday()
    {
        var report = StatusReportBuilder.Build(Noon, [Service()]);

        var days = report.Services.Single().Days;
        Assert.Equal(90, days.Count);
        Assert.Equal("2026-08-24", days[0].Date);
        Assert.Equal("2026-11-21", days[^1].Date);
    }

    [Fact]
    public void MidnightRolloverShiftsWindowByOneDay()
    {
        var beforeMidnight = StatusReportBuilder.Build(new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.Zero), [Service()]);
        var afterMidnight = StatusReportBuilder.Build(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), [Service()]);

        Assert.Equal("2026-11-20", beforeMidnight.Services.Single().Days[^1].Date);
        Assert.Equal("2026-11-21", afterMidnight.Services.Single().Days[^1].Date);
        Assert.Equal("2026-08-23", beforeMidnight.Services.Single().Days[0].Date);
        Assert.Equal("2026-08-24", afterMidnight.Services.Single().Days[0].Date);
    }

    [Fact]
    public void NoDataShowsEmptyStateWithoutInventedNumbers()
    {
        var report = StatusReportBuilder.Build(Noon, [Service()]);

        var service = report.Services.Single();
        Assert.Equal("nodata", service.State);
        Assert.Null(service.UptimePercent);
        Assert.Null(service.CoveragePercent);
        Assert.Null(service.LastCheckUtc);
        Assert.All(service.Days, day => Assert.Equal("nodata", day.State));
        Assert.Equal("unknown", report.OverallState);
    }

    [Fact]
    public void UnknownAndStartingAreExcludedFromUptime()
    {
        var days = new Dictionary<string, DayCounts>
        {
            ["2026-11-21"] = new() { Up = 9, Down = 1, Unknown = 50, Starting = 20, Stale = 10 },
        };
        var latest = new LatestRecord(CurrentSlot, ProbeState.Up, CurrentSlot + 1);

        var service = StatusReportBuilder.Build(Noon, [Service(firstSlot: TodayStart, latest: latest, days: days)])
            .Services.Single();

        Assert.Equal(90.0, service.UptimePercent);
    }

    [Fact]
    public void DegradedCountsAsAvailableAndIsFlaggedSeparately()
    {
        var days = new Dictionary<string, DayCounts>
        {
            ["2026-11-21"] = new() { Up = 5, Degraded = 5 },
        };
        var latest = new LatestRecord(CurrentSlot, ProbeState.Degraded, CurrentSlot + 1);

        var service = StatusReportBuilder.Build(Noon, [Service(firstSlot: TodayStart, latest: latest, days: days)])
            .Services.Single();

        Assert.Equal(100.0, service.UptimePercent);
        Assert.Equal(5, service.DegradedSlots);
        Assert.Equal("degraded", service.Days[^1].State);
        Assert.Equal("degraded", service.State);
    }

    [Fact]
    public void MissingSlotsAffectCoverageOnly()
    {
        // Монитор начал работать в полночь; наблюдено половина ожидаемых слотов.
        var expectedNow = StatusReportBuilder.ExpectedSlots(DateOnly.FromDateTime(Noon.UtcDateTime), TodayStart, CurrentSlot);
        var days = new Dictionary<string, DayCounts>
        {
            ["2026-11-21"] = new() { Up = expectedNow / 2 },
        };
        var latest = new LatestRecord(CurrentSlot, ProbeState.Up, CurrentSlot + 1);

        var service = StatusReportBuilder.Build(Noon, [Service(firstSlot: TodayStart, latest: latest, days: days)])
            .Services.Single();

        Assert.Equal(100.0, service.UptimePercent);
        Assert.True(service.CoveragePercent is > 49 and < 51, $"coverage {service.CoveragePercent}");
    }

    [Fact]
    public void ExpectedSlotsCountsOnlyFromStartOfMonitoringAndUpToNow()
    {
        var day = new DateOnly(2026, 11, 21);
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal(2880, StatusReportBuilder.ExpectedSlots(day, dayStart, dayStart + 86_370));
        Assert.Equal(1441, StatusReportBuilder.ExpectedSlots(day, dayStart, dayStart + 12 * 3600));
        Assert.Equal(1440, StatusReportBuilder.ExpectedSlots(day, dayStart + 15, dayStart + 12 * 3600));
        Assert.Equal(0, StatusReportBuilder.ExpectedSlots(day, null, dayStart + 3600));
        Assert.Equal(0, StatusReportBuilder.ExpectedSlots(day, dayStart + 2 * 86_400, dayStart + 3600));
    }

    [Fact]
    public void StaleMonitorIsNotShownAsCurrent()
    {
        var latest = new LatestRecord(CurrentSlot - 10 * 30, ProbeState.Up, CurrentSlot - 300);

        var service = StatusReportBuilder.Build(Noon, [Service(firstSlot: TodayStart, latest: latest)]).Services.Single();

        Assert.Equal("stale", service.State);
    }

    [Theory]
    [InlineData(ProbeState.Up, "operational")]
    [InlineData(ProbeState.Starting, "starting")]
    [InlineData(ProbeState.Unknown, "unknown")]
    [InlineData(ProbeState.Stale, "stale")]
    [InlineData(ProbeState.Down, "down")]
    public void CurrentStateMapsFromLatestResult(ProbeState state, string expected)
    {
        var latest = new LatestRecord(CurrentSlot, state, CurrentSlot + 1);

        var service = StatusReportBuilder.Build(Noon, [Service(firstSlot: TodayStart, latest: latest)]).Services.Single();

        Assert.Equal(expected, service.State);
    }

    [Fact]
    public void OverallStateSummarizesEnabledServicesOnly()
    {
        var up = new LatestRecord(CurrentSlot, ProbeState.Up, CurrentSlot + 1);
        var down = new LatestRecord(CurrentSlot, ProbeState.Down, CurrentSlot + 1);

        Assert.Equal("operational", StatusReportBuilder.Build(Noon, [
            Service("a", firstSlot: TodayStart, latest: up),
            Service("b", enabled: false),
        ]).OverallState);
        Assert.Equal("partial_outage", StatusReportBuilder.Build(Noon, [
            Service("a", firstSlot: TodayStart, latest: up),
            Service("b", firstSlot: TodayStart, latest: down),
        ]).OverallState);
        Assert.Equal("major_outage", StatusReportBuilder.Build(Noon, [
            Service("a", firstSlot: TodayStart, latest: down),
        ]).OverallState);
        Assert.Equal("unknown", StatusReportBuilder.Build(Noon, [Service("b", enabled: false)]).OverallState);
    }

    [Fact]
    public void DisabledServiceIsListedWithoutData()
    {
        var service = StatusReportBuilder.Build(Noon, [Service(enabled: false)]).Services.Single();

        Assert.Equal("disabled", service.State);
        Assert.Null(service.UptimePercent);
        Assert.Null(service.CoveragePercent);
    }

    [Fact]
    public void PublicJsonHasNoTargetsOrDiagnostics()
    {
        var latest = new LatestRecord(CurrentSlot, ProbeState.Down, CurrentSlot + 1);
        var report = StatusReportBuilder.Build(Noon, [
            Service("messages", firstSlot: TodayStart, latest: latest, days: new Dictionary<string, DayCounts>
            {
                ["2026-11-21"] = new() { Down = 3 },
            }),
        ]);

        var json = JsonSerializer.Serialize(report);

        Assert.DoesNotContain("://", json);
        Assert.DoesNotContain("example", json);
        Assert.DoesNotContain("Exception", json);
    }

    [Fact]
    public void CatalogMatchesPublicNodeComponents()
    {
        Assert.Equal(13, PublicServices.All.Count);
        Assert.Equal(PublicServices.All.Count, PublicServices.All.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var service in PublicServices.All)
        {
            Assert.False(string.IsNullOrEmpty(BarkFluff.Node.NodeSubdomains.Get(service.Key)));
        }
    }
}
