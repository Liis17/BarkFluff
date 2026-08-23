using BarkFluff.GrpcServer.XAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BarkFluff.GrpcServer;

public static class HealthEndpointExtensions
{
    /// <summary>
    /// /ping + /health/live + /health/ready. Readiness отдаёт кэш фоновых проверок зависимостей
    /// (ReadinessMonitorService): 200 для healthy/degraded/starting, 503 для down.
    /// Требует регистрации builder.Services.AddBarkFluffHealth() до Build.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPingEndpoint();

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "alive", instanceId = InstanceId.Current }))
            .AllowAnonymous();

        endpoints.MapGet("/health/ready", (ReadinessMonitorService monitor) =>
            {
                var snapshot = monitor.Snapshot;
                return snapshot.Status == "down"
                    ? Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Ok(snapshot);
            })
            .AllowAnonymous();

        return endpoints;
    }
}
