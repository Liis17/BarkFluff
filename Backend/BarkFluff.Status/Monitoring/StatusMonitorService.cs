using System.Collections.Concurrent;
using System.Globalization;
using BarkFluff.Status.Catalog;
using BarkFluff.Status.Configuration;
using BarkFluff.Status.Probing;
using BarkFluff.Status.Reporting;
using BarkFluff.Status.Storage;

namespace BarkFluff.Status.Monitoring;

/// <summary>
/// Опрос всех включённых сервисов сразу после старта и затем каждые 30 секунд (PeriodicTimer не даёт
/// циклам перекрываться). Результаты одного слота пишутся одной транзакцией; после записи пересобирается
/// отчёт, который отдаёт API — запросы к БД на каждый HTTP-запрос не выполняются.
/// </summary>
public sealed class StatusMonitorService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(StatusReportBuilder.IntervalSeconds);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(1);
    private const int Parallelism = 4;

    private readonly StatusOptions _options;
    private readonly StatusStore _store;
    private readonly ReadinessProbe _probe;
    private readonly StatusReportHolder _holder;
    private readonly TimeProvider _time;
    private readonly ILogger<StatusMonitorService> _logger;
    private readonly Dictionary<string, ProbeState> _lastStates = new(StringComparer.Ordinal);
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public StatusMonitorService(
        StatusOptions options,
        StatusStore store,
        ReadinessProbe probe,
        StatusReportHolder holder,
        TimeProvider time,
        ILogger<StatusMonitorService> logger)
    {
        _options = options;
        _store = store;
        _probe = probe;
        _holder = holder;
        _time = time;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Ошибка открытия БД должна остановить старт, а не молча отдавать пустой статус.
        _store.Initialize();
        Prune();
        _holder.Publish(BuildReport());
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            do
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Цикл проверки статуса завершился ошибкой; следующая попытка по расписанию");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Остановка хоста.
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var slot = StatusReportBuilder.SlotOf(now);
        var results = new ConcurrentBag<(string Service, ProbeRecord Record, string? ErrorClass)>();

        await Parallel.ForEachAsync(
            _options.ServiceOrigins,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (entry, token) =>
            {
                var outcome = await _probe.ProbeAsync(entry.Value, entry.Key, token);
                var record = new ProbeRecord(
                    Origin(entry.Value),
                    entry.Key,
                    slot,
                    outcome.State,
                    _time.GetUtcNow().ToUnixTimeSeconds(),
                    outcome.LatencyMs);
                results.Add((entry.Key, record, outcome.ErrorClass));
            });

        var inserted = _store.WriteBatch(results.Select(r => r.Record));
        foreach (var (service, record, errorClass) in results.OrderBy(r => r.Service, StringComparer.Ordinal))
        {
            if (errorClass is not null)
                _logger.LogDebug("Сервис {Service}: проверка недоступна ({ErrorClass})", service, errorClass);
            LogTransition(service, record.State);
        }
        _logger.LogDebug("Слот {Slot}: записано {Inserted} из {Total}", slot, inserted, results.Count);

        if (now - _lastPrune >= PruneEvery)
            Prune();

        _holder.Publish(BuildReport());
    }

    private void LogTransition(string service, ProbeState state)
    {
        if (_lastStates.TryGetValue(service, out var previous))
        {
            if (previous != state)
                _logger.LogInformation("Сервис {Service}: состояние {Previous} → {Current}", service, previous, state);
        }
        else
        {
            _logger.LogInformation("Сервис {Service}: первое состояние {Current}", service, state);
        }
        _lastStates[service] = state;
    }

    private void Prune()
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var oldest = today.AddDays(-(StatusReportBuilder.WindowDays - 1));
        var cutoffSlot = new DateTimeOffset(oldest.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        _store.Prune(cutoffSlot, oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        _lastPrune = _time.GetUtcNow();
    }

    private StatusReport BuildReport()
    {
        var now = _time.GetUtcNow();
        var fromDay = DateOnly.FromDateTime(now.UtcDateTime)
            .AddDays(-(StatusReportBuilder.WindowDays - 1))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var inputs = PublicServices.All.Select(service =>
        {
            if (!_options.ServiceOrigins.TryGetValue(service.Key, out var origin))
                return new ServiceInput(service.Key, service.Name, false, null, null, new Dictionary<string, DayCounts>());

            var endpoint = Origin(origin);
            return new ServiceInput(
                service.Key,
                service.Name,
                true,
                _store.FirstSlot(endpoint, service.Key),
                _store.Latest(endpoint, service.Key),
                _store.Days(endpoint, service.Key, fromDay));
        }).ToList();

        return StatusReportBuilder.Build(now, inputs);
    }

    /// <summary>Идентичность endpoint в истории: схема, хост и порт. Альтернативные ноды хранятся раздельно.</summary>
    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);
}
