using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

using MassTransit;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Фоновый сбор состояния сервисов платформы: liveness-пробы GET /health/live (fallback /ping
/// для образов без health-endpoint'ов; h2c для HTTP/2-only портов, HTTP/1.1 для mixed
/// listener'ов без TLS), readiness
/// GET /health/ready (кэш проверок зависимостей на стороне сервиса), статусы Docker-контейнеров,
/// свежесть логов в Seq для сервисов без HTTP-listener. Инфраструктура: RabbitMQ через
/// собственный MassTransit-бин панели, S3 через S3BrowserService, Postgres/Redis — агрегат
/// readiness-чеков сервисов с fallback на docker-state. Снимок хранится в памяти.
/// </summary>
public class HealthCollectorService : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SeqFreshnessWindow = TimeSpan.FromMinutes(5);
    private const string ProbeHttpClientName = "health-probes";
    private static readonly string[] CriticalServices = ["BarkFluff.Beacon", "BarkFluff.Identity", "BarkFluff.Updates"];

    private readonly IServiceProvider _serviceProvider;
    private readonly DockerService _dockerService;
    private readonly S3BrowserService _s3Browser;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCollectorService> _logger;
    private readonly Dictionary<string, (Uri BaseUri, bool UseHttp1)> _probes;

    private volatile HealthSnapshot? _snapshot;

    public HealthCollectorService(
        IServiceProvider serviceProvider,
        DockerService dockerService,
        S3BrowserService s3Browser,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HealthCollectorService> logger)
    {
        _serviceProvider = serviceProvider;
        _dockerService = dockerService;
        _s3Browser = s3Browser;
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
                // Docker недоступен — статус определяется пробами и свежестью логов в Seq
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
                var (probeUp, latencyMs, readiness) = hasProbe && probes.TryGetValue(svc.Name, out var probe)
                    ? (probe.LiveUp, probe.LatencyMs, probe.Readiness)
                    : ((bool?)null, (int?)null, (ReadinessHealth?)null);
                var lastSeen = hasProbe ? null : await lastSeenTasks[svc.Name];

                services.Add(new ServiceHealthStatus(
                    svc.Name,
                    svc.Container,
                    ResolveStatus(hasProbe, probeUp, readiness, dockerState, lastSeen, now),
                    hasProbe,
                    probeUp,
                    latencyMs,
                    dockerState,
                    lastSeen?.ToString("o"),
                    readiness));
            }

            var infrastructure = await BuildInfrastructureAsync(scope.ServiceProvider, dockerStates, services, ct);

            var healthy = services.Count(s => s.Status == "healthy");
            var degraded = services.Count(s => s.Status == "degraded");
            var down = services.Count(s => s.Status == "down");
            var unknown = services.Count(s => s.Status == "unknown");

            _snapshot = new HealthSnapshot(
                now,
                services,
                new HealthSummary(services.Count, healthy, degraded, down, unknown),
                ResolveSystemStatus(services),
                infrastructure);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCollector: collection failed");
        }
    }

    private sealed record ProbeResult(bool LiveUp, int? LatencyMs, ReadinessHealth? Readiness);

    private async Task<Dictionary<string, ProbeResult>> ProbeAllAsync(CancellationToken ct)
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
                var liveUp = false;
                try
                {
                    using var live = await client.GetAsync(new Uri(baseUri, "health/live"), ct);
                    liveUp = live.IsSuccessStatusCode;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Health live probe failed: {Service} ({Host})", kv.Key, baseUri);
                }

                ReadinessHealth? readiness = null;
                try
                {
                    using var ready = await client.GetAsync(new Uri(baseUri, "health/ready"), ct);
                    var body = await ready.Content.ReadAsStringAsync(ct);
                    readiness = ParseReadiness(body);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Health ready probe failed: {Service} ({Host})", kv.Key, baseUri);
                }

                // Образ без health-endpoint'ов: liveness определяется старым /ping
                if (!liveUp && readiness is null)
                {
                    try
                    {
                        using var ping = await client.GetAsync(new Uri(baseUri, "ping"), ct);
                        liveUp = ping.IsSuccessStatusCode;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Health ping fallback failed: {Service} ({Host})", kv.Key, baseUri);
                    }
                }

                return (kv.Key, new ProbeResult(liveUp, (int?)stopwatch.ElapsedMilliseconds, readiness));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.Item1, r => r.Item2);
    }

    private static ReadinessHealth? ParseReadiness(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var statusEl) ||
                statusEl.ValueKind != JsonValueKind.String)
                return null;

            var status = statusEl.GetString();
            if (status is not ("healthy" or "degraded" or "down" or "starting" or "unknown"))
                return null;

            var checks = new List<DependencyCheckDto>();
            if (root.TryGetProperty("checks", out var checksEl) && checksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var check in checksEl.EnumerateArray())
                {
                    if (check.ValueKind != JsonValueKind.Object ||
                        !check.TryGetProperty("name", out var nameEl) ||
                        !check.TryGetProperty("status", out var checkStatusEl))
                        continue;

                    checks.Add(new DependencyCheckDto(
                        nameEl.GetString() ?? "?",
                        checkStatusEl.GetString() ?? "unknown",
                        check.TryGetProperty("latencyMs", out var latency) && latency.ValueKind == JsonValueKind.Number
                            ? latency.GetInt64()
                            : null,
                        check.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                            ? error.GetString()
                            : null));
                }
            }

            return new ReadinessHealth(status, checks);
        }
        catch (JsonException)
        {
            return null;
        }
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

    private async Task<List<InfrastructureHealth>> BuildInfrastructureAsync(
        IServiceProvider scopeProvider,
        Dictionary<string, string>? dockerStates,
        IReadOnlyList<ServiceHealthStatus> services,
        CancellationToken ct)
    {
        var result = new List<InfrastructureHealth>();

        // RabbitMQ: собственный MassTransit-бин панели; fallback — docker-state контейнера
        var rabbitStatus = DockerFallback(dockerStates, "rabbitmq", "unknown");
        var rabbitSource = "docker";
        string? rabbitDetail = null;
        try
        {
            var bus = scopeProvider.GetService<IBusControl>();
            if (bus is not null)
            {
                rabbitStatus = bus.CheckHealth().Status == BusHealthStatus.Healthy ? "healthy" : "down";
                rabbitSource = "bus";
            }
        }
        catch (Exception ex)
        {
            rabbitStatus = "down";
            rabbitSource = "bus";
            rabbitDetail = ex.Message;
        }
        result.Add(new InfrastructureHealth("RabbitMQ", rabbitStatus, rabbitSource, rabbitDetail));

        // S3: прямой вызов через существующие клиенты панели; без настроенных бакетов — docker/unknown
        var s3Status = DockerFallback(dockerStates, "minio", "unknown");
        var s3Source = "docker";
        string? s3Detail = null;
        try
        {
            var elapsed = await _s3Browser.CheckHealthAsync(ct);
            if (elapsed is not null)
            {
                s3Status = "healthy";
                s3Source = "s3";
                s3Detail = $"{(int)elapsed.Value.TotalMilliseconds} мс";
            }
            else
            {
                s3Source = "none";
                s3Detail = "бакеты не настроены";
            }
        }
        catch (Exception ex)
        {
            s3Status = "down";
            s3Source = "s3";
            s3Detail = ex.Message;
        }
        result.Add(new InfrastructureHealth("S3", s3Status, s3Source, s3Detail));

        // PostgreSQL: агрегат EF-чеков из readiness сервисов; fallback — docker-state
        var efChecks = services
            .SelectMany(s => s.Readiness?.Checks ?? [])
            .Where(c => c.Name.EndsWith("Context", StringComparison.Ordinal))
            .ToList();
        if (efChecks.Count > 0)
        {
            var failed = efChecks.Count(c => c.Status != "healthy");
            result.Add(new InfrastructureHealth(
                "PostgreSQL",
                failed == 0 ? "healthy" : failed == efChecks.Count ? "down" : "degraded",
                "readiness",
                $"{efChecks.Count - failed}/{efChecks.Count} контекстов БД отвечают"));
        }
        else
        {
            result.Add(new InfrastructureHealth("PostgreSQL", DockerFallback(dockerStates, "postgres_barkfluff", "unknown"), "docker", null));
        }

        // Redis: агрегат Redis-чеков из readiness; fallback — docker-state
        var redisChecks = services
            .SelectMany(s => s.Readiness?.Checks ?? [])
            .Where(c => c.Name == "Redis")
            .ToList();
        if (redisChecks.Count > 0)
        {
            var failed = redisChecks.Count(c => c.Status != "healthy");
            result.Add(new InfrastructureHealth(
                "Redis",
                failed == 0 ? "healthy" : failed == redisChecks.Count ? "down" : "degraded",
                "readiness",
                null));
        }
        else
        {
            result.Add(new InfrastructureHealth("Redis", DockerFallback(dockerStates, "redis", "unknown"), "docker", null));
        }

        return result;
    }

    private static string DockerFallback(Dictionary<string, string>? dockerStates, string container, string fallback)
    {
        if (dockerStates is not null && dockerStates.TryGetValue(container, out var state))
        {
            return state switch
            {
                "running" => "healthy",
                "restarting" or "paused" => "degraded",
                "exited" or "dead" => "down",
                _ => fallback
            };
        }
        return fallback;
    }

    private static string ResolveStatus(
        bool hasProbe, bool? probeUp, ReadinessHealth? readiness, string? dockerState, DateTime? lastSeen, DateTime now)
    {
        if (hasProbe)
        {
            if (probeUp != true)
                return "down";

            return readiness?.Status switch
            {
                "down" => "down",
                "degraded" => "degraded",
                _ => "healthy"
            };
        }

        // CloudMessaging (worker без HTTP-listener): docker-state + свежесть Seq
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
