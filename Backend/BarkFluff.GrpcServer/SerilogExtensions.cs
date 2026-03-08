using BarkFluff.GrpcServer.Metrics;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;
using Serilog.Events;

namespace BarkFluff.GrpcServer;

public static class SerilogExtensions
{
    /// <summary>
    /// Настраивает Serilog с выводом в консоль и Seq.
    /// Вызывать ПОСЛЕ LoadConfiguration(), чтобы Seq URL из конфигурации был доступен.
    /// </summary>
    public static WebApplicationBuilder AddBarkFluffSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";

        builder.Host.UseSerilog((context, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", serviceName)
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Seq(seqUrl,
                    bufferBaseFilename: "logs/seq-buffer",
                    bufferSizeLimitBytes: 104857600,
                    batchPostingLimit: 100,
                    period: TimeSpan.FromSeconds(2),
                    queueSizeLimit: 100000);
        });

        return builder;
    }

    /// <summary>
    /// Регистрирует MetricsCollector и MetricsReporterService.
    /// </summary>
    public static IServiceCollection AddBarkFluffMetrics(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton<MetricsCollector>();
        services.AddHostedService(sp =>
            new MetricsReporterService(
                sp.GetRequiredService<MetricsCollector>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MetricsReporterService>>(),
                serviceName));

        return services;
    }
}
