using System.Diagnostics;
using System.Text.Json;

using BarkFluff.GrpcServer;

using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace BarkFluff.GrpcServer.Tests;

public class LognaraLoggingTests
{
    [Fact]
    public void Formatter_PreservesCapturedTraceAndStructuredMetricsAfterActivityEnds()
    {
        var sink = new CaptureSink();
        string traceId;
        string spanId;
        string parentSpanId;
        using (var parent = new Activity("parent").SetIdFormat(ActivityIdFormat.W3C).Start())
        using (var activity = new Activity("operation").Start())
        using (LogContext.PushProperty("RequestId", "request-42"))
        using (LogContext.PushProperty("CorrelationId", "business-42"))
        using (var logger = new LoggerConfiguration().Enrich.FromLogContext()
                   .Enrich.With<ActivityLogEnricher>().WriteTo.Sink(sink).CreateLogger())
        {
            traceId = activity.TraceId.ToHexString();
            spanId = activity.SpanId.ToHexString();
            parentSpanId = parent.SpanId.ToHexString();
            logger.Error(new InvalidOperationException("failure"), "ServiceMetrics {@Metrics}",
                new { SchemaVersion = 2, Counters = new { messages_sent = 3 }, Gauges = new { active = 4 } });
        }

        using var output = new StringWriter();
        new LognaraJsonFormatter().Format(sink.Event!, output);
        using var json = JsonDocument.Parse(output.ToString());
        var evt = json.RootElement;
        Assert.Equal(traceId, evt.GetProperty("trace_id").GetString());
        Assert.Equal(spanId, evt.GetProperty("span_id").GetString());
        Assert.Equal(parentSpanId, evt.GetProperty("parent_span_id").GetString());
        Assert.Equal("request-42", evt.GetProperty("request_id").GetString());
        Assert.Equal("Error", evt.GetProperty("level").GetString());
        var attributes = evt.GetProperty("attributes");
        Assert.Equal("business-42", attributes.GetProperty("CorrelationId").GetString());
        Assert.Equal(2, attributes.GetProperty("Metrics").GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(3, attributes.GetProperty("Metrics").GetProperty("Counters").GetProperty("messages_sent").GetInt32());
        Assert.Equal(typeof(InvalidOperationException).FullName, attributes.GetProperty("exception.type").GetString());
        Assert.Contains("failure", attributes.GetProperty("exception.stacktrace").GetString());
    }

    [Fact]
    public async Task SelectedLognaraSink_SendsCanonicalJsonArrayAndNeverCreatesSeqBuffer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "barkfluff-logging-tests", Guid.NewGuid().ToString("N"));
        var received = new TaskCompletionSource<(string Path, string? ContentType, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var agent = builder.Build();
        agent.MapPost("/v1/logs", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            received.TrySetResult((context.Request.Path, context.Request.ContentType, body));
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });
        await agent.StartAsync();
        try
        {
            var config = Configuration(new Dictionary<string, string?>
            {
                ["Logging:Backend"] = "Lognara",
                ["Lognara:AgentUrl"] = agent.Urls.Single() + "/v1/logs",
            });
            using (var logger = new LoggerConfiguration().WriteToBarkFluffBackend(config, directory).CreateLogger())
            using (var activity = new Activity("send").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                logger.Information("Delivery test");
                var receivedEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
                using var json = JsonDocument.Parse(receivedEvent.Body);
                Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
                Assert.Equal("Delivery test", json.RootElement[0].GetProperty("message").GetString());
                Assert.Equal(activity.TraceId.ToHexString(), json.RootElement[0].GetProperty("trace_id").GetString());
                Assert.Equal("/v1/logs", receivedEvent.Path);
                Assert.StartsWith("application/json", receivedEvent.ContentType);
                Assert.Empty(Directory.GetFiles(directory, "seq-buffer*"));
            }
        }
        finally
        {
            await agent.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Seq")]
    public async Task DefaultAndExplicitSeq_SendQueryableTraceProperties(string? backend)
    {
        var directory = Path.Combine(Path.GetTempPath(), "barkfluff-logging-tests", Guid.NewGuid().ToString("N"));
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var seq = builder.Build();
        seq.MapPost("/api/events/raw", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.TrySetResult(await reader.ReadToEndAsync());
            context.Response.StatusCode = StatusCodes.Status200OK;
        });
        await seq.StartAsync();
        try
        {
            var config = Configuration(new Dictionary<string, string?>
            {
                ["Logging:Backend"] = backend,
                ["Seq:ServerUrl"] = seq.Urls.Single(),
            });
            using (var logger = new LoggerConfiguration().Enrich.With<ActivityLogEnricher>()
                       .WriteToBarkFluffBackend(config, directory).CreateLogger())
            using (var activity = new Activity("send").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                logger.ForContext("RequestId", "request").Information("Delivery test");
                var body = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
                using var json = JsonDocument.Parse(body.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
                Assert.Equal(activity.TraceId.ToHexString(), json.RootElement.GetProperty("@tr").GetString());
                Assert.Equal(activity.TraceId.ToHexString(), json.RootElement.GetProperty("TraceId").GetString());
                Assert.Equal(activity.SpanId.ToHexString(), json.RootElement.GetProperty("SpanId").GetString());
                Assert.Equal("request", json.RootElement.GetProperty("RequestId").GetString());
                Assert.Empty(Directory.GetFiles(directory, "lognara-buffer*"));
            }
        }
        finally
        {
            await seq.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Other", null)]
    [InlineData("Lognara", "not-a-url")]
    public void InvalidConfiguration_FailsInsteadOfSilentlyLosingLogs(string backend, string? url)
    {
        var config = Configuration(new Dictionary<string, string?>
        {
            ["Logging:Backend"] = backend,
            ["Lognara:AgentUrl"] = url,
        });
        Assert.Throws<InvalidOperationException>(() => new LoggerConfiguration().WriteToBarkFluffBackend(config));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class CaptureSink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }

}
