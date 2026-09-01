extern alias GrpcServer;

using BarkFluff.Settings.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

using MetricsCollector = GrpcServer::BarkFluff.GrpcServer.Metrics.MetricsCollector;

namespace BarkFluff.Settings.Infrastructure;

public sealed class SettingsStartupInitializer
{
    private const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SettingsStartupInitializer> _logger;

    public SettingsStartupInitializer(
        IServiceScopeFactory scopeFactory,
        MetricsCollector metrics,
        ILogger<SettingsStartupInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _metrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _metrics.Set("db_healthy", 0);

        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<SettingsContext>();

                _metrics.Increment("db_migration_attempts");
                if (context.Database.IsRelational())
                    await context.Database.MigrateAsync(cancellationToken);

                var seeder = scope.ServiceProvider.GetRequiredService<SettingsSeeder>();
                await seeder.SeedAsync(cancellationToken);
                await seeder.ValidateAsync(cancellationToken);

                _metrics.Increment("db_migration_succeeded");
                _metrics.Set("db_healthy", 1);
                _logger.LogInformation("Settings database migrations applied successfully");
                return;
            }
            catch (Exception exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _metrics.Increment("db_migration_failed");
                _metrics.Increment("settings_bootstrap_errors_total");
                _logger.LogWarning(
                    exception,
                    "Settings database initialization failed; retrying in {DelaySeconds} seconds ({Attempt}/{MaxAttempts})",
                    delay.TotalSeconds,
                    attempt,
                    MaxAttempts);
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
            catch
            {
                _metrics.Increment("db_migration_failed");
                _metrics.Increment("settings_bootstrap_errors_total");
                _metrics.Set("db_healthy", 0);
                throw;
            }
        }
    }
}
