using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class LogsClearEndpoints
{
    public record StartClearRequest(string Scope);

    public static void MapLogsClearEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq/clear")
            .WithTags("LogsClear");

        // POST /api/seq/clear/start
        group.MapPost("/start", (
            StartClearRequest request,
            LogsClearService clearService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var scope = request.Scope?.Equals("old", StringComparison.OrdinalIgnoreCase) == true
                ? LogsClearScope.Old
                : LogsClearScope.All;

            var jobId = clearService.StartClear(scope);
            return Results.Ok(new { jobId });
        })
        .WithName("StartLogsClear")
        .WithOpenApi();

        // GET /api/seq/clear/{jobId}/status
        group.MapGet("/{jobId:guid}/status", (
            Guid jobId,
            LogsClearService clearService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var job = clearService.GetJob(jobId);
            if (job is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                state = job.State.ToString().ToLowerInvariant(),
                scope = job.Scope.ToString().ToLowerInvariant(),
                totalCount = job.TotalCount,
                deletedCount = job.DeletedCount,
                error = job.Error
            });
        })
        .WithName("GetLogsClearStatus")
        .WithOpenApi();
    }
}
