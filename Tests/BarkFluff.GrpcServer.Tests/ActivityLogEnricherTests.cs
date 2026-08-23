using BarkFluff.GrpcServer;

using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

using System.Diagnostics;

namespace BarkFluff.GrpcServer.Tests;

public class ActivityLogEnricherTests
{
    [Fact]
    public void Enrich_AddsTraceSpanAndCorrelationIds()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var logEvent = CreateLogEvent();

        new ActivityLogEnricher().Enrich(logEvent, new PropertyFactory());

        Assert.Equal(activity.TraceId.ToString(), Scalar(logEvent, "TraceId"));
        Assert.Equal(activity.SpanId.ToString(), Scalar(logEvent, "SpanId"));
        Assert.Equal(activity.TraceId.ToString(), Scalar(logEvent, "CorrelationId"));
    }

    [Fact]
    public void Enrich_DoesNotReplaceExplicitCorrelationId()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var logEvent = CreateLogEvent(new LogEventProperty("CorrelationId", new ScalarValue("custom")));

        new ActivityLogEnricher().Enrich(logEvent, new PropertyFactory());

        Assert.Equal("custom", Scalar(logEvent, "CorrelationId"));
    }

    private static LogEvent CreateLogEvent(params LogEventProperty[] properties) => new(
        DateTimeOffset.UtcNow,
        LogEventLevel.Information,
        exception: null,
        new MessageTemplate(Array.Empty<MessageTemplateToken>()),
        properties);

    private static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed class PropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
