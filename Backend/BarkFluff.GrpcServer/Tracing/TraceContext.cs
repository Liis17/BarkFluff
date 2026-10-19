using System.Diagnostics;

using Serilog.Context;

namespace BarkFluff.GrpcServer.Tracing;

/// <summary>Restores persisted causal context without changing public message payloads.</summary>
public static class TraceContext
{
    private static readonly AsyncLocal<string?> Correlation = new();

    public static string? CorrelationId => Correlation.Value;

    public static IDisposable PushCorrelationId(string? value) => new CorrelationScope(value);

    public static IDisposable StartDispatch(string operation, string? traceParent, string? traceState, string? correlationId)
        => new DispatchScope(operation, traceParent, traceState, correlationId);

    private sealed class CorrelationScope : IDisposable
    {
        private readonly string? _previous = Correlation.Value;
        private readonly IDisposable? _logScope;

        public CorrelationScope(string? value)
        {
            Correlation.Value = value;
            if (!string.IsNullOrWhiteSpace(value))
                _logScope = LogContext.PushProperty("CorrelationId", value);
        }

        public void Dispose()
        {
            _logScope?.Dispose();
            Correlation.Value = _previous;
        }
    }

    private sealed class DispatchScope : IDisposable
    {
        private readonly Activity? _previous = Activity.Current;
        private readonly Activity _activity;
        private readonly IDisposable _correlation;

        public DispatchScope(string operation, string? traceParent, string? traceState, string? correlationId)
        {
            // A batch can contain unrelated operations; never inherit the dispatcher's ambient trace.
            Activity.Current = null;
            _activity = new Activity(operation).SetIdFormat(ActivityIdFormat.W3C);
            if (ActivityContext.TryParse(traceParent, traceState, out var parent))
            {
                _activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
                _activity.TraceStateString = parent.TraceState;
            }
            _activity.Start();
            _correlation = PushCorrelationId(correlationId);
        }

        public void Dispose()
        {
            _correlation.Dispose();
            _activity.Dispose();
            Activity.Current = _previous;
        }
    }
}
