using BarkFluff.Status.Reporting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BarkFluff.Status.Api;

public static class StatusEndpoints
{
    /// <summary>GET /api/status — единственный API. Входных параметров нет: публичный запрос ничего не меняет.</summary>
    public static IEndpointRouteBuilder MapStatusApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/status", (StatusReportHolder holder, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return holder.Current is { } report
                ? Results.Json(report)
                : Results.Json(new { error = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        return endpoints;
    }
}
