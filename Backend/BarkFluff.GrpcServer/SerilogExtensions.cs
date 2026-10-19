using BarkFluff.GrpcServer.Metrics;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Http;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.GrpcServer;

public static class SerilogExtensions
{
    /// <summary>
    /// Настраивает Serilog: Seq или Lognara (дисковый батч-синк), Console — полностью
    /// в Development и только Warning+ в Production. Синхронный stdout-синк не блокирует
    /// горячий путь в проде, но `docker logs` по-прежнему показывает предупреждения/ошибки.
    /// Вызывать ПОСЛЕ LoadConfiguration(), чтобы Seq URL из конфигурации был доступен.
    /// </summary>
    public static WebApplicationBuilder AddBarkFluffSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        ValidateLogBackend(builder.Configuration);
        var isDevelopment = builder.Environment.IsDevelopment();

        const string consoleTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] [TraceId={TraceId} SpanId={SpanId}] {Message:lj}{NewLine}{Exception}";

        builder.Host.UseSerilog((context, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.With<ActivityLogEnricher>()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", serviceName);

            loggerConfig.WriteToBarkFluffBackend(context.Configuration);

            if (isDevelopment)
            {
                loggerConfig.WriteTo.Console(outputTemplate: consoleTemplate);
            }
            else
            {
                loggerConfig.WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    outputTemplate: consoleTemplate);
            }
        });

        return builder;
    }

    public static LoggerConfiguration WriteToBarkFluffBackend(
        this LoggerConfiguration loggerConfig,
        IConfiguration configuration,
        string bufferDirectory = "logs",
        IHttpClient? lognaraHttpClient = null)
    {
        ValidateLogBackend(configuration);
        if (string.Equals(configuration["Logging:Backend"], "Lognara", StringComparison.OrdinalIgnoreCase))
        {
            return loggerConfig.WriteTo.DurableHttpUsingFileSizeRolledBuffers(
                requestUri: configuration["Lognara:AgentUrl"] ?? "http://127.0.0.1:7400/v1/logs",
                bufferBaseFileName: Path.Combine(bufferDirectory, "lognara-buffer"),
                bufferFileSizeLimitBytes: 10 * 1024 * 1024,
                retainedBufferFileCountLimit: 10,
                logEventLimitBytes: 1024 * 1024,
                logEventsInBatchLimit: 100,
                batchSizeLimitBytes: 1536 * 1024,
                period: TimeSpan.FromSeconds(2),
                textFormatter: new LognaraJsonFormatter(),
                httpClient: lognaraHttpClient);
        }

        return loggerConfig.WriteTo.Seq(configuration["Seq:ServerUrl"] ?? "http://seq:5341",
            bufferBaseFilename: Path.Combine(bufferDirectory, "seq-buffer"),
            bufferSizeLimitBytes: 104857600,
            batchPostingLimit: 100,
            period: TimeSpan.FromSeconds(2),
            queueSizeLimit: 100000);
    }

    private static void ValidateLogBackend(IConfiguration configuration)
    {
        var backend = configuration["Logging:Backend"] ?? "Seq";
        if (!string.Equals(backend, "Seq", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "Lognara", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Logging:Backend must be Seq or Lognara.");

        if (string.Equals(backend, "Lognara", StringComparison.OrdinalIgnoreCase) &&
            configuration["Lognara:AgentUrl"] is { } url &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidOperationException("Lognara:AgentUrl must be an absolute HTTP(S) URL.");
    }

    /// <summary>
    /// Регистрирует MetricsCollector и MetricsReporterService.
    /// </summary>
    public static IServiceCollection AddBarkFluffMetrics(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton(new MetricsCollector(MetricsExportProfiles.ForService(serviceName)));
        services.AddHostedService(sp =>
            new MetricsReporterService(
                sp.GetRequiredService<MetricsCollector>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MetricsReporterService>>(),
                serviceName));

        return services;
    }
}

internal static class MetricsExportProfiles
{
    public static MetricsExportProfile ForService(string serviceName) => serviceName switch
    {
        "BarkFluff.Messages" or "BarkFluff.Files" or "BarkFluff.Updates" or "BarkFluff.Onliner" or
        "BarkFluff.CloudMessaging" or "BarkFluff.Federation" or "BarkFluff.ClientStorage" or
        "BarkFluff.Web" or "BarkFluff.Developers" => MetricsExportProfile.BufferAll(),
        "BarkFluff.Bots" => MetricsExportProfile.ImmediateByDefault(
            "bot_api_messages_sent", "bot_updates_stored"),
        "BarkFluff.WebServer" => MetricsExportProfile.ImmediateByDefault(
            "http_requests_total", "http_requests_errors"),
        _ => MetricsExportProfile.ImmediateByDefault()
    };
}
