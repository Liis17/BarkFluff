using System.Diagnostics;
using System.Net;
using System.Text.Json;

using BarkFluff.GrpcServer;

using Microsoft.Extensions.Configuration;

using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Http;

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
        var client = new CaptureHttpClient();
        try
        {
            var config = Configuration(new Dictionary<string, string?>
            {
                ["Logging:Backend"] = "Lognara",
                ["Lognara:AgentUrl"] = "http://localhost:7400/v1/logs",
            });
            using (var logger = new LoggerConfiguration().WriteToBarkFluffBackend(config, directory, client).CreateLogger())
            using (var activity = new Activity("send").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                logger.Information("Delivery test");
                var body = await client.Body.Task.WaitAsync(TimeSpan.FromSeconds(15));
                using var json = JsonDocument.Parse(body);
                Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
                Assert.Equal("Delivery test", json.RootElement[0].GetProperty("message").GetString());
                Assert.Equal(activity.TraceId.ToHexString(), json.RootElement[0].GetProperty("trace_id").GetString());
                Assert.Equal("http://localhost:7400/v1/logs", client.RequestUri);
                Assert.Empty(Directory.GetFiles(directory, "seq-buffer*"));
            }
        }
        finally
        {
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

    private sealed class CaptureHttpClient : IHttpClient
    {
        public TaskCompletionSource<string> Body { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? RequestUri { get; private set; }
        public void Configure(IConfiguration configuration) { }
        public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken)
        {
            RequestUri = requestUri;
            using var reader = new StreamReader(contentStream);
            Body.TrySetResult(await reader.ReadToEndAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
        public void Dispose() { }
    }
}
