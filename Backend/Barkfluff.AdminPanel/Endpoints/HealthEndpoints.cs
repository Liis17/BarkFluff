using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/health")
            .WithTags("Health");

        group.MapGet("/overview", (HealthCollectorService collector) =>
            {
                var snapshot = collector.GetSnapshot();
                return snapshot is null ? Results.StatusCode(503) : Results.Ok(snapshot);
            })
            .WithName("GetHealthOverview")
            .WithOpenApi();
    }
}
