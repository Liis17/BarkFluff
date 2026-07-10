using BarkFluff.Configuration.Host;
using BarkFluff.Configuration.Infrastructure;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

using Serilog;

namespace BarkFluff.Configuration;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddBarkFluffSerilog("BarkFluff.Configuration");
        builder.Services.AddBarkFluffMetrics("BarkFluff.Configuration");

        var envPort = Environment.GetEnvironmentVariable("CONFIGURATION_PORT")
                      ?? Environment.GetEnvironmentVariable("RunSettings__Port");

        if (int.TryParse(envPort, out var dynamicPort))
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(dynamicPort, o =>
                {
                    o.Protocols = HttpProtocols.Http2;
                });
            });
        }
        else
        {
            builder.SetRunningAddress(builder.Configuration);
        }

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });

        builder.Services.AddGrpcReflection();

        var host = builder.Configuration["CONFIGURATION_HOST"];
        var port = builder.Configuration["CONFIGURATION_DBPORT"];
        var database = builder.Configuration["CONFIGURATION_DATABASE"];
        var username = builder.Configuration["CONFIGURATION_USERNAME"];
        var password = builder.Configuration["CONFIGURATION_PASSWORD"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Database settings are not configured. Set CONFIGURATION_HOST, CONFIGURATION_DATABASE, CONFIGURATION_USERNAME, CONFIGURATION_PASSWORD.");
        }

        var configurationDb = string.IsNullOrWhiteSpace(port)
            ? $"Host={host};Database={database};Username={username};Password={password}"
            : $"Host={host};Port={port};Database={database};Username={username};Password={password}";

        builder.Services.AddDbContext<ConfigurationContext>(c
            => c.UseNpgsql(configurationDb, npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(30);
            }));

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddScoped<ConfigurationStorage>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ConfigurationContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var metrics = scope.ServiceProvider.GetRequiredService<MetricsCollector>();

            // Стартовые gauge-метрики
            metrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            metrics.Set("db_healthy", 0);

            var retryCount = 0;
            const int maxRetries = 5;
            var delay = TimeSpan.FromSeconds(2);

            while (retryCount < maxRetries)
            {
                try
                {
                    logger.LogInformation("Attempting to connect to database and apply migrations (attempt {Attempt}/{MaxRetries})...", retryCount + 1, maxRetries);
                    metrics.Increment("db_migration_attempts");
                    ctx.Database.Migrate();
                    metrics.Increment("db_migration_succeeded");
                    metrics.Set("db_healthy", 1);
                    logger.LogInformation("Database migrations applied successfully.");

                    // Авто-заполнение пустых конфигураций значениями по умолчанию
                    try
                    {
                        // Извлекаем данные postgres из connection string Configuration-сервиса
                        var pgHostOnly = host.Contains(':') ? host.Split(':')[0] : host;

                        // Креды RabbitMQ пробрасываются в контейнер через env_file: .env,
                        // совпадают с RABBITMQ_DEFAULT_USER/PASS, с которыми стартует брокер
                        var rabbitUsername = builder.Configuration["RABBITMQ_DEFAULT_USER"] ?? "guest";
                        var rabbitPassword = builder.Configuration["RABBITMQ_DEFAULT_PASS"] ?? "guest";

                        var populatorLogger = scope.ServiceProvider.GetRequiredService<ILogger<ConfigurationDefaultsPopulator>>();
                        var populator = new ConfigurationDefaultsPopulator(
                            ctx, populatorLogger, pgHostOnly, username, password,
                            rabbitUsername, rabbitPassword, metrics);

                        populator.PopulateDefaultsAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception populateEx)
                    {
                        metrics.Increment("defaults_populator_failed");
                        logger.LogWarning(populateEx,
                            "Ошибка при авто-заполнении конфигураций. Сервис продолжит работу.");
                    }

                    break;
                }
                catch (Npgsql.NpgsqlException ex)
                {
                    retryCount++;
                    metrics.Increment("db_migration_failed");
                    if (retryCount >= maxRetries)
                    {
                        metrics.Set("db_healthy", 0);
                        logger.LogError(ex, "Failed to connect to database after {MaxRetries} attempts. Please ensure PostgreSQL is running and connection settings are correct.", maxRetries);
                        throw;
                    }

                    logger.LogWarning(ex, "Failed to connect to database. Retrying in {Delay} seconds... (attempt {Attempt}/{MaxRetries})", delay.TotalSeconds, retryCount + 1, maxRetries);
                    Thread.Sleep(delay);
                    delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                }
            }
        }

        app.MapGrpcReflectionService();
        app.UseRouting();

        app.MapGrpcService<ConfigurationApiService>();

        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
        app.Run();
    }
}