using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Фоновый сбор liveness-статусов сервисов платформы: анонимный GET /ping (h2c) на основных
/// listener'ах, статусы Docker-контейнеров, свежесть логов в Seq как fallback для сервисов без /ping.
/// Снимок хранится в памяти — API отвечает мгновенно и по сети на запрос не ходит.
/// </summary>
public class HealthCollectorService : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SeqFreshnessWindow = TimeSpan.FromMinutes(5);
    private const string ProbeHttpClientName = "health-probes";
    private static readonly string[] CriticalServices = ["BarkFluff.Beacon", "BarkFluff.Identity", "BarkFluff.Updates"];

    private readonly IServiceProvider _serviceProvider;
    private readonly DockerService _dockerService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCollectorService> _logger;
    private readonly Dictionary<string, (Uri BaseUri, bool UseHttp1)> _probes;

    private volatile HealthSnapshot? _snapshot;

    public HealthCollectorService(
        IServiceProvider serviceProvider,
        DockerService dockerService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HealthCollectorService> logger)
    {
        _serviceProvider = serviceProvider;
        _dockerService = dockerService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _probes = PlatformServiceRegistry.BarkFluff
            .Where(s => s.ProbeDefaultHost is not null)
            .ToDictionary(
                s => s.Name,
                s => (new Uri(configuration[s.ProbeConfigKey!] ?? s.ProbeDefaultHost!), s.ProbeHttp1));
    }

    public HealthSnapshot? GetSnapshot() => _snapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectAsync(stoppingToken);

        using var timer = new PeriodicTimer(CollectionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CollectAsync(stoppingToken);
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        try
        {
            Dictionary<string, string>? dockerStates = null;
            try
            {
                var containers = await _dockerService.GetContainersAsync();
                dockerStates = containers
                    .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.State))
                    .GroupBy(c => c.Name.TrimStart('/'), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().State, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Docker недоступен — статус определяется пробой и свежестью логов в Seq
            }

            using var scope = _serviceProvider.CreateScope();
            var seq = scope.ServiceProvider.GetRequiredService<SeqService>();

            var probeTask = ProbeAllAsync(ct);
            var lastSeenTasks = PlatformServiceRegistry.BarkFluff
                .Where(s => !_probes.ContainsKey(s.Name))
                .ToDictionary(s => s.Name, s => GetLastSeenAsync(seq, s.Name));
            var probes = await probeTask;

            var now = DateTime.UtcNow;
            var services = new List<ServiceHealthStatus>(PlatformServiceRegistry.BarkFluff.Count);

            foreach (var svc in PlatformServiceRegistry.BarkFluff)
            {
                string? dockerState = null;
                dockerStates?.TryGetValue(svc.Container, out dockerState);

                var hasProbe = _probes.ContainsKey(svc.Name);
                var (probeUp, latencyMs) = hasProbe && probes.TryGetValue(svc.Name, out var probe)
                    ? (probe.Up, probe.LatencyMs)
                    : ((bool?)null, (int?)null);
                var lastSeen = hasProbe ? null : await lastSeenTasks[svc.Name];

                services.Add(new ServiceHealthStatus(
                    svc.Name,
                    svc.Container,
                    ResolveStatus(hasProbe, probeUp, dockerState, lastSeen, now),
                    hasProbe,
                    probeUp,
                    latencyMs,
                    dockerState,
                    lastSeen?.ToString("o")));
            }

            var healthy = services.Count(s => s.Status == "healthy");
            var degraded = services.Count(s => s.Status == "degraded");
            var down = services.Count(s => s.Status == "down");
            var unknown = services.Count(s => s.Status == "unknown");

            _snapshot = new HealthSnapshot(
                now,
                services,
                new HealthSummary(services.Count, healthy, degraded, down, unknown),
                ResolveSystemStatus(services));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCollector: collection failed");
        }
    }

    private async Task<Dictionary<string, (bool Up, int? LatencyMs)>> ProbeAllAsync(CancellationToken ct)
    {
        var h2cClient = _httpClientFactory.CreateClient(ProbeHttpClientName);
        var http1Client = _httpClientFactory.CreateClient(ProbeHttpClientName + "-http1");

        var tasks = _probes.Select(async kv =>
        {
            var (baseUri, useHttp1) = kv.Value;
            var client = useHttp1 ? http1Client : h2cClient;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(new Uri(baseUri, "ping"), ct);
                return (kv.Key, response.IsSuccessStatusCode, (int?)stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (kv.Key, false, (int?)null);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health probe failed: {Service} ({Host})", kv.Key, baseUri);
                return (kv.Key, false, (int?)null);
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.Item1, r => (r.Item2, r.Item3));
    }

    private static async Task<DateTime?> GetLastSeenAsync(SeqService seq, string application)
    {
        var response = await seq.GetEventsAsync($"Application = '{application}'", count: 1);
        var events = response is null ? null : SeqService.ExtractEventsArray(response.Value);
        if (events is not { Count: > 0 })
            return null;

        var evt = events[0];
        if (evt.ValueKind != JsonValueKind.Object ||
            !evt.TryGetProperty("Timestamp", out var ts) ||
            ts.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(ts.GetString(), null, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string ResolveStatus(
        bool hasProbe, bool? probeUp, string? dockerState, DateTime? lastSeen, DateTime now)
    {
        if (hasProbe)
            return probeUp == true ? "healthy" : "down";

        if (dockerState is not null)
        {
            return dockerState switch
            {
                "running" => "healthy",
                "restarting" or "paused" => "degraded",
                "exited" or "dead" => "down",
                _ => "unknown"
            };
        }

        return lastSeen is not null && now - lastSeen < SeqFreshnessWindow ? "healthy" : "unknown";
    }

    private static string ResolveSystemStatus(IReadOnlyList<ServiceHealthStatus> services)
    {
        if (services.Any(s => CriticalServices.Contains(s.Name) && s.Status == "down"))
            return "down";
        if (services.Any(s => s.Status is "down" or "degraded"))
            return "degraded";
        if (services.Any(s => s.Status == "healthy"))
            return "healthy";
        return "unknown";
    }
}
