using BarkFluff.Status.Probing;
using BarkFluff.Status.Storage;

namespace BarkFluff.Status.Reporting;

/// <summary>Публичный отчёт. Без адресов целей, трасс и текстов ошибок проверок.</summary>
public sealed record StatusReport(
    DateTimeOffset GeneratedAtUtc,
    int WindowDays,
    int IntervalSeconds,
    string OverallState,
    IReadOnlyList<ServiceReport> Services);

public sealed record ServiceReport(
    string Key,
    string Name,
    string State,
    DateTimeOffset? LastCheckUtc,
    double? UptimePercent,
    double? CoveragePercent,
    long DegradedSlots,
    IReadOnlyList<DayReport> Days);

/// <summary>Один день окна: State — худшее наблюдённое состояние дня, Observed/Expected — слоты.</summary>
public sealed record DayReport(string Date, string State, long Observed, long Expected);

/// <summary>Вход билдера по одному сервису: данные уже прочитаны из хранилища.</summary>
public sealed record ServiceInput(
    string Key,
    string Name,
    bool Enabled,
    long? FirstSlot,
    LatestRecord? Latest,
    IReadOnlyDictionary<string, DayCounts> Days);

/// <summary>
/// Сборка отчёта из агрегатов. Чистая функция: время передаётся явно, запросов к БД нет.
/// Доступность = (up + degraded) / (up + degraded + down); unknown, starting и stale исключены.
/// Coverage = наблюдённые слоты / ожидаемые слоты от начала мониторинга; пропуски влияют только на coverage.
/// </summary>
public static class StatusReportBuilder
{
    public const int WindowDays = 90;
    public const int IntervalSeconds = 30;

    // Последний слот старше трёх интервалов — монитор не пишет результаты, сервис нельзя показывать как текущий.
    private const long MonitorStaleSlots = 3;

    public static StatusReport Build(DateTimeOffset now, IReadOnlyList<ServiceInput> services)
    {
        var currentSlot = SlotOf(now);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var first = today.AddDays(-(WindowDays - 1));

        var reports = new List<ServiceReport>(services.Count);
        foreach (var service in services)
        {
            var days = new List<DayReport>(WindowDays);
            long observed = 0, expected = 0, covered = 0, available = 0, down = 0, degraded = 0;

            for (var day = first; day <= today; day = day.AddDays(1))
            {
                var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var counts = service.Days.GetValueOrDefault(key, DayCounts.Empty);
                var slots = ExpectedSlots(day, service.FirstSlot, currentSlot);

                observed += counts.Observed;
                expected += slots;
                covered += Math.Min(counts.Observed, slots);
                available += counts.Up + counts.Degraded;
                down += counts.Down;
                degraded += counts.Degraded;
                days.Add(new DayReport(key, DayState(counts), counts.Observed, slots));
            }

            var enabled = service.Enabled;
            reports.Add(new ServiceReport(
                service.Key,
                service.Name,
                enabled ? CurrentState(service.Latest, currentSlot) : "disabled",
                enabled && service.Latest is { } latest ? DateTimeOffset.FromUnixTimeSeconds(latest.CheckedAtUtc) : null,
                enabled && available + down > 0 ? Percent(available, available + down) : null,
                enabled && expected > 0 ? Percent(covered, expected) : null,
                degraded,
                days));
        }

        return new StatusReport(now, WindowDays, IntervalSeconds, OverallState(reports), reports);
    }

    /// <summary>Ожидаемые слоты дня (0, 30, … с кратной 30 секунды), от первого слота мониторинга и не позже текущего.</summary>
    public static long ExpectedSlots(DateOnly day, long? firstSlot, long currentSlot)
    {
        if (firstSlot is not { } from)
            return 0;

        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var dayEndExclusive = dayStart + 86_400;
        var lowest = ((Math.Max(dayStart, from) + IntervalSeconds - 1) / IntervalSeconds) * IntervalSeconds;
        var highest = Math.Min(dayEndExclusive - IntervalSeconds, currentSlot);
        return highest < lowest ? 0 : (highest - lowest) / IntervalSeconds + 1;
    }

    public static long SlotOf(DateTimeOffset now) =>
        now.ToUnixTimeSeconds() / IntervalSeconds * IntervalSeconds;

    private static string CurrentState(LatestRecord? latest, long currentSlot)
    {
        if (latest is null)
            return "nodata";
        if (currentSlot - latest.SlotUtc > MonitorStaleSlots * IntervalSeconds)
            return "stale";
        return latest.State switch
        {
            ProbeState.Up => "operational",
            ProbeState.Degraded => "degraded",
            ProbeState.Down => "down",
            ProbeState.Starting => "starting",
            ProbeState.Unknown => "unknown",
            _ => "stale",
        };
    }

    private static string DayState(DayCounts counts)
    {
        if (counts.Observed == 0)
            return "nodata";
        if (counts.Down > 0)
            return "down";
        if (counts.Degraded > 0)
            return "degraded";
        if (counts.Up > 0)
            return "operational";
        if (counts.Stale > 0)
            return "stale";
        if (counts.Unknown > 0)
            return "unknown";
        return "starting";
    }

    private static string OverallState(IReadOnlyList<ServiceReport> services)
    {
        var states = services.Where(s => s.State != "disabled").Select(s => s.State).ToList();
        if (states.Count == 0)
            return "unknown";
        if (states.All(s => s == "down"))
            return "major_outage";
        if (states.Any(s => s == "down"))
            return "partial_outage";
        if (states.Any(s => s == "degraded"))
            return "degraded";
        if (states.Any(s => s is "unknown" or "stale" or "nodata" or "starting"))
            return "unknown";
        return "operational";
    }

    private static double Percent(long part, long whole) =>
        Math.Round(part * 100.0 / whole, 2, MidpointRounding.AwayFromZero);
}
