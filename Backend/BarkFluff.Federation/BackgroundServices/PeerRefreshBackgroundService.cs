using System.Collections.Concurrent;

using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.BackgroundServices;

// Фоновый рефреш KnownServers (docs/rearch/03-discovery.md, "Политика обновления"): раз в сутки
// для Active непринудительных (не-Manual) пиров, Unreachable — экспоненциальный backoff.
// Счётчик подряд-неудач — in-memory (как троттлинг регистрации в Navigator): потеря состояния
// при рестарте безвредна, худший случай — один лишний цикл до повторной пометки Unreachable.
public class PeerRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    private const int UnreachableThreshold = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FederationSwitch _switch;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<PeerRefreshBackgroundService> _logger;
    private readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();

    public PeerRefreshBackgroundService(IServiceScopeFactory scopeFactory, FederationSwitch federationSwitch, MetricsCollector metrics, ILogger<PeerRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _switch = federationSwitch;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            try
            {
                await RefreshDuePeersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка фонового рефреша пиров Federation");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshDuePeersAsync(CancellationToken ct)
    {
        // P1-04: выключенная/несконфигурированная нода не ведёт сетевой refresh пиров.
        if (!_switch.IsActive)
            return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FederationContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();

        var candidates = await context.KnownServers
            .Where(s => s.Source != KnownServerSource.Manual
                        && (s.Status == KnownServerStatus.Active || s.Status == KnownServerStatus.Unreachable))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var server in candidates)
        {
            var failures = _consecutiveFailures.GetOrAdd(server.ServerName, 0);
            var backoff = server.Status == KnownServerStatus.Unreachable
                ? TimeSpan.FromHours(Math.Min(24, Math.Pow(2, failures)))
                : RefreshInterval;

            var due = server.LastKeyRefreshAt == null || now - server.LastKeyRefreshAt >= backoff;
            if (!due)
                continue;

            var resolved = await resolver.ResolveAsync(server.ServerName, ct);

            if (resolved != null)
            {
                _consecutiveFailures[server.ServerName] = 0;

                if (server.Status == KnownServerStatus.Unreachable)
                {
                    server.Status = KnownServerStatus.Active;
                    await context.SaveChangesAsync(ct);
                    _logger.LogInformation("Пир {Server} снова Active после фонового рефреша", server.ServerName);
                }
            }
            else
            {
                var newCount = _consecutiveFailures.AddOrUpdate(server.ServerName, 1, (_, v) => v + 1);

                if (newCount >= UnreachableThreshold && server.Status != KnownServerStatus.Unreachable)
                {
                    server.Status = KnownServerStatus.Unreachable;
                    await context.SaveChangesAsync(ct);
                    _logger.LogWarning("Пир {Server} помечен Unreachable после {Count} неудач подряд", server.ServerName, newCount);
                }
            }
        }

        var activeCount = await context.KnownServers.CountAsync(s => s.Status == KnownServerStatus.Active, ct);
        _metrics.Set("known_servers_active", activeCount);
    }
}
