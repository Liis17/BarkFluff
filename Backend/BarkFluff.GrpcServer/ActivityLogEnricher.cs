using Serilog.Core;
using Serilog.Events;

using System.Diagnostics;

namespace BarkFluff.GrpcServer;

/// <summary>
/// Adds W3C activity identifiers to every log emitted while a request, gRPC call,
/// message-consumer activity, or outgoing dependency activity is current.
/// Explicit business correlation identifiers are preserved.
/// </summary>
public sealed class ActivityLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        var traceId = activity.TraceId.ToString();
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", traceId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", traceId));
    }
}
