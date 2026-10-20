using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Events;

namespace BarkFluff.GrpcServer;

internal sealed record BarkFluffLoggingOptions(
    bool LognaraEnable,
    bool SeqEnable,
    bool ConsoleOutput,
    Uri? LognaraAgentUri,
    Uri? SeqServerUri,
    string? AgentExecutable,
    string ServiceName)
{
    internal const string DefaultLognaraAgentUrl = "http://127.0.0.1:7400/v1/logs";
    internal const string EmbeddedLognaraAgentUrl = "http://127.0.0.1:7400/v1/logs";

    internal static BarkFluffLoggingOptions Resolve(
        IConfiguration configuration,
        string serviceName,
        Func<string, string?>? commandLineValue = null,
        Func<string, string?>? environmentValue = null)
    {
        var read = CreateReader(configuration, commandLineValue, environmentValue);

        var lognaraEnable = ReadFlag("LognaraEnable", read("LognaraEnable"), defaultValue: false);
        var seqEnable = ReadFlag("SeqEnable", read("SeqEnable"), defaultValue: true);
        var consoleOutput = ReadFlag("ConsoleOutput", read("ConsoleOutput"), defaultValue: true);

        Uri? lognaraAgentUri = null;
        string? agentExecutable = null;
        if (lognaraEnable)
        {
            var agentUrl = read("Lognara:AgentUrl") ?? DefaultLognaraAgentUrl;
            lognaraAgentUri = ParseHttpUrl("Lognara:AgentUrl", agentUrl);
            agentExecutable = read("Lognara:AgentExecutable");
            if (string.IsNullOrWhiteSpace(agentExecutable))
            {
                agentExecutable = null;
            }
            else
            {
                agentExecutable = agentExecutable.Trim();
                var embeddedUrl = new Uri(EmbeddedLognaraAgentUrl, UriKind.Absolute);
                if (!Uri.Equals(lognaraAgentUri, embeddedUrl))
                    throw new InvalidOperationException(
                        "Lognara:AgentUrl must use the local endpoint when Lognara:AgentExecutable is configured.");
            }
        }

        Uri? seqServerUri = null;
        if (seqEnable)
        {
            seqServerUri = ParseHttpUrl("Seq:ServerUrl", read("Seq:ServerUrl") ?? "http://seq:5341");
        }

        return new BarkFluffLoggingOptions(
            lognaraEnable,
            seqEnable,
            consoleOutput,
            lognaraAgentUri,
            seqServerUri,
            agentExecutable,
            serviceName);
    }

    internal static bool ResolveConsoleOutput(
        IConfiguration configuration,
        Func<string, string?>? commandLineValue = null,
        Func<string, string?>? environmentValue = null) =>
        ReadFlag("ConsoleOutput", CreateReader(configuration, commandLineValue, environmentValue)("ConsoleOutput"), defaultValue: true);

    private static Func<string, string?> CreateReader(
        IConfiguration configuration,
        Func<string, string?>? commandLineValue,
        Func<string, string?>? environmentValue)
    {
        if (commandLineValue is null)
        {
            var providers = (configuration as IConfigurationRoot)?.Providers
                .OfType<CommandLineConfigurationProvider>()
                .Reverse()
                .ToArray() ?? [];
            commandLineValue = key =>
            {
                foreach (var provider in providers)
                {
                    if (provider.TryGet(key, out var value))
                        return value;
                }

                return null;
            };
        }

        environmentValue ??= key => Environment.GetEnvironmentVariable(key);

        return key =>
        {
            var commandLine = commandLineValue(key);
            if (commandLine is not null)
                return commandLine;

            var environmentKey = key.Replace(":", "__", StringComparison.Ordinal);
            var environment = environmentValue(environmentKey);
            if (environment is not null)
                return environment;

            if (!string.Equals(key, environmentKey, StringComparison.Ordinal))
            {
                environment = environmentValue(key);
                if (environment is not null)
                    return environment;
            }

            return configuration[key];
        };
    }

    private static bool ReadFlag(string key, string? value, bool defaultValue)
    {
        if (value is null)
            return defaultValue;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new InvalidOperationException($"{key} must be true or false.");
    }

    private static Uri ParseHttpUrl(string key, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{key} must be an absolute HTTP(S) URL without user information or a fragment.");

        return uri;
    }
}

public static class SerilogExtensions
{
    private const string ConsoleTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] [TraceId={TraceId} SpanId={SpanId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Configures the independent Lognara, Seq, and console logging sinks.</summary>
    public static WebApplicationBuilder AddBarkFluffSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        var options = BarkFluffLoggingOptions.Resolve(builder.Configuration, serviceName);
        builder.Services.AddSingleton(options);
        builder.Services.AddHostedService<LognaraAgentLifecycleService>();
        builder.Services.AddBarkFluffTracing();

        builder.Host.UseSerilog((_, loggerConfig) =>
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

            WriteToBarkFluffSinks(loggerConfig, options);
        });

        return builder;
    }

    public static IServiceCollection AddBarkFluffTracing(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, RequestTraceStartupFilter>());
        services.AddHostedService<TraceDiagnostics>();
        return services;
    }

    public static LoggerConfiguration WriteToBarkFluffBackend(
        this LoggerConfiguration loggerConfig,
        IConfiguration configuration,
        string bufferDirectory = "logs")
    {
        var options = BarkFluffLoggingOptions.Resolve(configuration, "BarkFluff");
        return WriteToBarkFluffSinks(loggerConfig, options, bufferDirectory);
    }

    internal static LoggerConfiguration WriteToBarkFluffSinks(
        LoggerConfiguration loggerConfig,
        BarkFluffLoggingOptions options,
        string bufferDirectory = "logs")
    {
        if (options.SeqEnable)
        {
            loggerConfig.WriteTo.Seq(options.SeqServerUri!.AbsoluteUri,
                bufferBaseFilename: Path.Combine(bufferDirectory, "seq-buffer"),
                bufferSizeLimitBytes: 104857600,
                batchPostingLimit: 100,
                period: TimeSpan.FromSeconds(2),
                queueSizeLimit: 100000);
        }

        if (options.LognaraEnable)
        {
            loggerConfig.WriteTo.Lognara(options.LognaraAgentUri!.AbsoluteUri,
                bufferBaseFileName: Path.Combine(bufferDirectory, "lognara-buffer"));
        }

        if (options.ConsoleOutput)
        {
            loggerConfig.WriteTo.Console(outputTemplate: ConsoleTemplate);
        }

        return loggerConfig;
    }

    /// <summary>Registers MetricsCollector and MetricsReporterService.</summary>
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
