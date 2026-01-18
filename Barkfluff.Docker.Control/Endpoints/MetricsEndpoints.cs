using System.Text;
using Barkfluff.Docker.Control.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;

namespace Barkfluff.Docker.Control.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1");

        group.MapGet("/health", HealthCheck);
        group.MapPost("/metrics", ReceiveMetrics);
        group.MapPost("/nodes/heartbeat", NodeHeartbeat);

        return app;
    }

    private static Ok<object> HealthCheck()
    {
        return TypedResults.Ok<object>(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    private static async Task<Results<Ok<ExportMetricsServiceResponse>, UnauthorizedHttpResult, BadRequest<string>>> ReceiveMetrics(
        HttpContext context,
        IOptions<OtlpSettings> otlpSettings,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(nameof(MetricsEndpoints));

        // Check if OTLP is enabled
        if (!otlpSettings.Value.Enabled)
        {
            return TypedResults.BadRequest("OTLP endpoint is disabled");
        }

        // Validate API key if configured
        if (!string.IsNullOrEmpty(otlpSettings.Value.ApiKey))
        {
            if (!context.Request.Headers.TryGetValue("x-api-key", out var providedKey) ||
                providedKey != otlpSettings.Value.ApiKey)
            {
                return TypedResults.Unauthorized();
            }
        }

        try
        {
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var request = ExportMetricsServiceRequest.Parser.ParseFrom(memoryStream);

            LogMetricsToConsole(request, logger, context.Connection.RemoteIpAddress?.ToString());

            return TypedResults.Ok(new ExportMetricsServiceResponse());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing OTLP metrics request");
            return TypedResults.BadRequest("Invalid protobuf data");
        }
    }

    private static async Task<Results<Ok<object>, UnauthorizedHttpResult, BadRequest<string>>> NodeHeartbeat(
        HttpContext context,
        IOptions<OtlpSettings> otlpSettings,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(nameof(MetricsEndpoints));

        // Check if OTLP is enabled
        if (!otlpSettings.Value.Enabled)
        {
            return TypedResults.BadRequest("OTLP endpoint is disabled");
        }

        // Validate API key if configured
        if (!string.IsNullOrEmpty(otlpSettings.Value.ApiKey))
        {
            if (!context.Request.Headers.TryGetValue("x-api-key", out var providedKey) ||
                providedKey != otlpSettings.Value.ApiKey)
            {
                return TypedResults.Unauthorized();
            }
        }

        try
        {
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var request = ExportMetricsServiceRequest.Parser.ParseFrom(memoryStream);

            LogHeartbeatToConsole(request, logger, context.Connection.RemoteIpAddress?.ToString());

            return TypedResults.Ok<object>(new { status = "heartbeat received", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing OTLP heartbeat request");
            return TypedResults.BadRequest("Invalid protobuf data");
        }
    }

    private static void LogMetricsToConsole(ExportMetricsServiceRequest request, ILogger logger, string? clientIp)
    {
        var originalColor = Console.ForegroundColor;
        var timestamp = DateTime.Now;

        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n=== OTLP Metrics Received at {timestamp:yyyy-MM-dd HH:mm:ss.fff} ===");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Client IP: {clientIp ?? "unknown"}");
            Console.WriteLine($"Resource Metrics Count: {request.ResourceMetrics.Count}");

            foreach (var resourceMetric in request.ResourceMetrics)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Resource:");
                Console.ForegroundColor = ConsoleColor.White;
                LogAttributes(resourceMetric.Resource.Attributes, indent: 4);

                foreach (var scopeMetric in resourceMetric.ScopeMetrics)
                {
                    LogScope(scopeMetric, indent: 2);
                }
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== End of OTLP Metrics ===\n");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }

        logger.LogInformation("Received OTLP metrics from {ClientIp} with {ResourceCount} resource metrics",
            clientIp ?? "unknown", request.ResourceMetrics.Count);
    }

    private static void LogHeartbeatToConsole(ExportMetricsServiceRequest request, ILogger logger, string? clientIp)
    {
        var originalColor = Console.ForegroundColor;
        var timestamp = DateTime.Now;

        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n=== Node Heartbeat at {timestamp:yyyy-MM-dd HH:mm:ss.fff} ===");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Client IP: {clientIp ?? "unknown"}");

            foreach (var resourceMetric in request.ResourceMetrics)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("  Node Information:");
                Console.ForegroundColor = ConsoleColor.White;

                var serviceName = GetAttributeValue(resourceMetric.Resource.Attributes, "service.name");
                var serviceInstance = GetAttributeValue(resourceMetric.Resource.Attributes, "service.instance.id");
                var serviceVersion = GetAttributeValue(resourceMetric.Resource.Attributes, "service.version");
                var hostName = GetAttributeValue(resourceMetric.Resource.Attributes, "host.name");
                var hostArch = GetAttributeValue(resourceMetric.Resource.Attributes, "host.arch");
                var osType = GetAttributeValue(resourceMetric.Resource.Attributes, "os.type");
                var processId = GetAttributeValue(resourceMetric.Resource.Attributes, "process.pid");
                var processRuntime = GetAttributeValue(resourceMetric.Resource.Attributes, "process.runtime.name");
                var processRuntimeVersion = GetAttributeValue(resourceMetric.Resource.Attributes, "process.runtime.version");
                var telemetrySdkLanguage = GetAttributeValue(resourceMetric.Resource.Attributes, "telemetry.sdk.language");
                var telemetrySdkName = GetAttributeValue(resourceMetric.Resource.Attributes, "telemetry.sdk.name");
                var telemetrySdkVersion = GetAttributeValue(resourceMetric.Resource.Attributes, "telemetry.sdk.version");

                if (!string.IsNullOrEmpty(serviceName))
                    Console.WriteLine($"    Service: {serviceName} {(!string.IsNullOrEmpty(serviceVersion) ? $"v{serviceVersion}" : "")}");
                if (!string.IsNullOrEmpty(serviceInstance))
                    Console.WriteLine($"    Instance ID: {serviceInstance}");
                if (!string.IsNullOrEmpty(hostName))
                    Console.WriteLine($"    Host: {hostName} {(!string.IsNullOrEmpty(hostArch) ? $"({hostArch})" : "")}");
                if (!string.IsNullOrEmpty(osType))
                    Console.WriteLine($"    OS: {osType}");
                if (!string.IsNullOrEmpty(processId))
                    Console.WriteLine($"    Process ID: {processId}");
                if (!string.IsNullOrEmpty(processRuntime))
                    Console.WriteLine($"    Runtime: {processRuntime} {(!string.IsNullOrEmpty(processRuntimeVersion) ? processRuntimeVersion : "")}");
                if (!string.IsNullOrEmpty(telemetrySdkName))
                    Console.WriteLine($"    Telemetry SDK: {telemetrySdkName} {(!string.IsNullOrEmpty(telemetrySdkLanguage) ? $"({telemetrySdkLanguage})" : "")} {(!string.IsNullOrEmpty(telemetrySdkVersion) ? $"v{telemetrySdkVersion}" : "")}");

                var metricCount = resourceMetric.ScopeMetrics.Sum(sm => sm.Metrics.Count);
                Console.WriteLine($"    Metrics: {metricCount}");
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("=== End of Heartbeat ===\n");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }

        logger.LogInformation("Received heartbeat from {ClientIp}", clientIp ?? "unknown");
    }

    private static void LogScope(ScopeMetrics scopeMetric, int indent)
    {
        var indentStr = new string(' ', indent);
        var scopeName = !string.IsNullOrEmpty(scopeMetric.Scope.Name) ? scopeMetric.Scope.Name : "unknown";
        var scopeVersion = !string.IsNullOrEmpty(scopeMetric.Scope.Version) ? $" v{scopeMetric.Scope.Version}" : "";

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"{indentStr}Scope: {scopeName}{scopeVersion}");
        Console.ForegroundColor = ConsoleColor.White;

        foreach (var metric in scopeMetric.Metrics)
        {
            LogMetric(metric, indent + 2);
        }
    }

    private static void LogMetric(Metric metric, int indent)
    {
        var indentStr = new string(' ', indent);
        var dataPointType = GetDataPointType(metric);
        var temporality = GetAggregationTemporality(metric);

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indentStr}Metric: {metric.Name}");
        if (!string.IsNullOrEmpty(metric.Description))
            Console.WriteLine($"{indentStr}  Description: {metric.Description}");
        if (!string.IsNullOrEmpty(metric.Unit))
            Console.WriteLine($"{indentStr}  Unit: {metric.Unit}");
        Console.WriteLine($"{indentStr}  Type: {dataPointType}{(!string.IsNullOrEmpty(temporality) ? $" ({temporality})" : "")}");

        switch (metric.DataCase)
        {
            case Metric.DataOneofCase.Gauge:
                LogNumberDataPoints(metric.Gauge.DataPoints, indent + 4);
                break;
            case Metric.DataOneofCase.Sum:
                LogNumberDataPoints(metric.Sum.DataPoints, indent + 4);
                break;
            case Metric.DataOneofCase.Histogram:
                LogHistogramDataPoints(metric.Histogram.DataPoints, metric.Histogram.AggregationTemporality, indent + 4);
                break;
            case Metric.DataOneofCase.ExponentialHistogram:
                Console.WriteLine($"{indentStr}  Exponential Histogram (skipped detailed output)");
                break;
            case Metric.DataOneofCase.Summary:
                LogSummaryDataPoints(metric.Summary.DataPoints, indent + 4);
                break;
        }
    }

    private static string GetDataPointType(Metric metric)
    {
        return metric.DataCase switch
        {
            Metric.DataOneofCase.Gauge => "Gauge",
            Metric.DataOneofCase.Sum => "Sum",
            Metric.DataOneofCase.Histogram => "Histogram",
            Metric.DataOneofCase.ExponentialHistogram => "ExponentialHistogram",
            Metric.DataOneofCase.Summary => "Summary",
            _ => "Unknown"
        };
    }

    private static string? GetAggregationTemporality(Metric metric)
    {
        var temporality = metric.DataCase switch
        {
            Metric.DataOneofCase.Sum => metric.Sum.AggregationTemporality,
            Metric.DataOneofCase.Histogram => metric.Histogram.AggregationTemporality,
            Metric.DataOneofCase.ExponentialHistogram => metric.ExponentialHistogram.AggregationTemporality,
            _ => global::OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Unspecified
        };

        return temporality switch
        {
            global::OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Delta => "Delta",
            global::OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Cumulative => "Cumulative",
            _ => null
        };
    }

    private static void LogNumberDataPoints(IEnumerable<NumberDataPoint> dataPoints, int indent)
    {
        var indentStr = new string(' ', indent);
        foreach (var dp in dataPoints)
        {
            var valueStr = dp.ValueCase switch
            {
                NumberDataPoint.ValueOneofCase.AsDouble => dp.AsDouble.ToString("F2"),
                NumberDataPoint.ValueOneofCase.AsInt => dp.AsInt.ToString(),
                _ => "null"
            };

            var attributesStr = FormatAttributes(dp.Attributes);
            var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)dp.TimeUnixNano / 1_000_000_000)
                                 .AddTicks((long)(dp.TimeUnixNano % 1_000_000_000) / 100);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{indentStr}{attributesStr}: {valueStr} @ {timestamp:yyyy-MM-dd HH:mm:ss.fff}");
        }
    }

    private static void LogHistogramDataPoints(IEnumerable<HistogramDataPoint> dataPoints, AggregationTemporality temporality, int indent)
    {
        var indentStr = new string(' ', indent);
        var temporalityStr = temporality switch
        {
            global::OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Delta => "Delta",
            global::OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Cumulative => "Cumulative",
            _ => "Unspecified"
        };

        foreach (var dp in dataPoints)
        {
            var attributesStr = FormatAttributes(dp.Attributes);
            var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)dp.TimeUnixNano / 1_000_000_000)
                                 .AddTicks((long)(dp.TimeUnixNano % 1_000_000_000) / 100);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{indentStr}{attributesStr}: count={dp.Count}, sum={dp.Sum:F2} @ {timestamp:yyyy-MM-dd HH:mm:ss.fff}");

            // Only show min/max if they have valid values (check if the optional field has a value)
            var hasMin = dp.HasMin;
            var hasMax = dp.HasMax;
            if (hasMin || hasMax)
            {
                var minStr = hasMin ? dp.Min.ToString("F2") : "N/A";
                var maxStr = hasMax ? dp.Max.ToString("F2") : "N/A";
                Console.WriteLine($"{indentStr}  ({temporalityStr}, min={minStr}, max={maxStr})");
            }
            else
            {
                Console.WriteLine($"{indentStr}  ({temporalityStr})");
            }

            if (dp.ExplicitBounds.Count > 0 && dp.BucketCounts.Count > 0)
            {
                Console.WriteLine($"{indentStr}  Buckets:");
                for (int i = 0; i < dp.ExplicitBounds.Count; i++)
                {
                    Console.WriteLine($"{indentStr}    ({dp.ExplicitBounds[i]}]: {dp.BucketCounts[i]}");
                }
                // The +Inf bucket
                if (dp.BucketCounts.Count > dp.ExplicitBounds.Count)
                {
                    Console.WriteLine($"{indentStr}    (+Inf]: {dp.BucketCounts[dp.BucketCounts.Count - 1]}");
                }
            }
        }
    }

    private static void LogSummaryDataPoints(IEnumerable<SummaryDataPoint> dataPoints, int indent)
    {
        var indentStr = new string(' ', indent);
        foreach (var dp in dataPoints)
        {
            var attributesStr = FormatAttributes(dp.Attributes);
            var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)dp.TimeUnixNano / 1_000_000_000)
                                 .AddTicks((long)(dp.TimeUnixNano % 1_000_000_000) / 100);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{indentStr}{attributesStr}: count={dp.Count}, sum={dp.Sum:F2} @ {timestamp:yyyy-MM-dd HH:mm:ss.fff}");

            if (dp.QuantileValues.Count > 0)
            {
                Console.WriteLine($"{indentStr}  Quantiles:");
                foreach (var qv in dp.QuantileValues)
                {
                    Console.WriteLine($"{indentStr}    p{(qv.Quantile * 100):F0}: {qv.Value:F2}");
                }
            }
        }
    }

    private static void LogAttributes(IEnumerable<KeyValue> attributes, int indent)
    {
        var indentStr = new string(' ', indent);
        foreach (var attr in attributes)
        {
            var valueStr = FormatAttributeValue(attr.Value);
            Console.WriteLine($"{indentStr}{attr.Key}: {valueStr}");
        }
    }

    private static string FormatAttributes(IEnumerable<KeyValue> attributes)
    {
        if (!attributes.Any()) return "{}";

        var sb = new StringBuilder("{");
        var first = true;
        foreach (var attr in attributes.Take(5)) // Limit to 5 attributes for brevity
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(attr.Key);
            sb.Append("=");
            sb.Append(FormatAttributeValue(attr.Value));
        }
        if (attributes.Count() > 5)
        {
            sb.Append(", ...");
        }
        sb.Append("}");
        return sb.ToString();
    }

    private static string FormatAttributeValue(AnyValue value)
    {
        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => $"\"{value.StringValue}\"",
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue.ToString(),
            AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(),
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString("F2"),
            AnyValue.ValueOneofCase.BytesValue => $"<{value.BytesValue.Length} bytes>",
            AnyValue.ValueOneofCase.ArrayValue => $"[{value.ArrayValue.Values.Count} items]",
            AnyValue.ValueOneofCase.KvlistValue => $"<{value.KvlistValue.Values.Count} pairs>",
            _ => "null"
        };
    }

    private static string? GetAttributeValue(IEnumerable<KeyValue> attributes, string key)
    {
        return attributes.FirstOrDefault(a => a.Key == key)?.Value?.StringValue;
    }
}
