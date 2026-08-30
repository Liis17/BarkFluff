using System.Diagnostics;

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

using BarkFluff.GrpcServer.XAuth;

namespace BarkFluff.GrpcServer;

/// <summary>Статус одной зависимости: healthy | down.</summary>
public sealed record DependencyCheck(string Name, string Status, long? LatencyMs, string? Error);

/// <summary>
/// Снимок readiness: healthy | degraded | down | starting | unknown.
/// degraded — часть зависимостей недоступна, down — все.
/// </summary>
public sealed record ReadinessSnapshot(
    string Status,
    DateTime CheckedAtUtc,
    IReadOnlyList<DependencyCheck> Checks,
    string InstanceId);

/// <summary>
/// Фоновая проверка зависимостей сервиса (EF Core, MassTransit/RabbitMQ, Redis, S3) раз в 15 секунд.
/// Зависимости обнаруживаются из DI автоматически — сервис ничего не конфигурирует.
/// /health/ready отдаёт кэш снимка и по сети на запрос не ходит.
/// </summary>
public class ReadinessMonitorService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _provider;
    private readonly ILogger<ReadinessMonitorService> _logger;

    public ReadinessMonitorService(IServiceProvider provider, ILogger<ReadinessMonitorService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public ReadinessSnapshot Snapshot { get; private set; } =
        new("starting", DateTime.UtcNow, [], InstanceId.Current);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CollectAsync(stoppingToken);
    }

    private static volatile Type[]? _dbContextTypes;

    // AddDbContext<TContext> регистрирует только конкретный тип (не базовый DbContext),
    // поэтому обнаруживаем зарегистрированные контексты рефлексией по загруженным сборкам
    // и резолвим их из скоупа.
    private static Type[] GetDbContextTypes(IServiceProvider scopedProvider)
    {
        var types = _dbContextTypes;
        if (types is not null)
            return types;

        types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsClass && !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .Where(t => scopedProvider.GetService(t) is not null)
            .ToArray();

        _dbContextTypes = types;
        return types;
    }

    private static Type[] SafeGetTypes(System.Reflection.Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
        catch { return Type.EmptyTypes; }
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        try
        {
            var checks = new List<DependencyCheck>();

            using var scope = _provider.CreateScope();
            var sp = scope.ServiceProvider;

            foreach (var contextType in GetDbContextTypes(sp))
            {
                if (sp.GetService(contextType) is not DbContext context)
                    continue;

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(CheckTimeout);
                    var connected = await context.Database.CanConnectAsync(cts.Token);
                    checks.Add(new DependencyCheck(
                        context.GetType().Name,
                        connected ? "healthy" : "down",
                        stopwatch.ElapsedMilliseconds,
                        connected ? null : "database is not accepting connections"));
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    checks.Add(new DependencyCheck(context.GetType().Name, "down", null, "connection check timed out"));
                }
                catch (Exception ex)
                {
                    checks.Add(new DependencyCheck(context.GetType().Name, "down", null, ex.Message));
                }
            }

            var bus = sp.GetService<IBusControl>();
            if (bus is not null)
            {
                var busHealth = bus.CheckHealth();
                checks.Add(new DependencyCheck(
                    "RabbitMQ",
                    busHealth.Status == BusHealthStatus.Healthy ? "healthy" : "down",
                    null,
                    null));
            }

            foreach (var redis in sp.GetServices<IConnectionMultiplexer>())
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var ping = await WithTimeout(redis.GetDatabase().PingAsync(), CheckTimeout);
                    checks.Add(new DependencyCheck("Redis", "healthy", stopwatch.ElapsedMilliseconds, null));
                }
                catch (Exception ex)
                {
                    checks.Add(new DependencyCheck("Redis", "down", null, ex.Message));
                }
            }

            var s3 = sp.GetService<Amazon.S3.IAmazonS3>();
            if (s3 is not null)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(CheckTimeout);
                    await s3.ListBucketsAsync(cts.Token);
                    checks.Add(new DependencyCheck("S3", "healthy", stopwatch.ElapsedMilliseconds, null));
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    checks.Add(new DependencyCheck("S3", "down", null, "check timed out"));
                }
                catch (Exception ex)
                {
                    checks.Add(new DependencyCheck("S3", "down", null, ex.Message));
                }
            }

            foreach (var contributor in sp.GetServices<IBarkFluffReadinessContributor>())
            {
                try
                {
                    checks.Add(await contributor.CheckAsync(ct));
                }
                catch (Exception ex)
                {
                    checks.Add(new DependencyCheck(contributor.GetType().Name, "down", null, ex.Message));
                }
            }

            var status = checks.Count == 0 || checks.All(c => c.Status == "healthy")
                ? "healthy"
                : checks.All(c => c.Status == "down")
                    ? "down"
                    : "degraded";

            Snapshot = new ReadinessSnapshot(status, DateTime.UtcNow, checks, InstanceId.Current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadinessMonitor: check cycle failed");
            Snapshot = new ReadinessSnapshot("unknown", DateTime.UtcNow, [], InstanceId.Current);
        }
    }

    private static async Task<TResult> WithTimeout<TResult>(Task<TResult> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
            throw new TimeoutException($"check timed out after {timeout.TotalSeconds:0}s");
        return await task;
    }
}
