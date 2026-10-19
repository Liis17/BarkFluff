using System.Diagnostics;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Serilog.Context;

namespace BarkFluff.GrpcServer.Tracing;

/// <summary>Request scopes enclose HTTP and all gRPC shapes, including exception interceptors.</summary>
public sealed class RequestTraceStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            using var fallback = Activity.Current is null
                ? StartFallback(context.Request.Headers["traceparent"].FirstOrDefault(),
                    context.Request.Headers["tracestate"].FirstOrDefault())
                : null;
            using var requestScope = LogContext.PushProperty("RequestId", context.TraceIdentifier);
            using var correlationScope = TraceContext.PushCorrelationId(
                context.Request.Headers["X-Correlation-ID"].FirstOrDefault());
            await continuation(context);
        });
        next(app);
    };

    private static Activity StartFallback(string? traceParent, string? traceState)
    {
        var activity = new Activity("BarkFluff.Request").SetIdFormat(ActivityIdFormat.W3C);
        if (ActivityContext.TryParse(traceParent, traceState, out var parent))
        {
            activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
            activity.TraceStateString = parent.TraceState;
        }
        return activity.Start();
    }
}
