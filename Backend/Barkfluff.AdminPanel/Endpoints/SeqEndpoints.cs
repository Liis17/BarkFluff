using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class SeqEndpoints
{
    private static readonly string[] KnownServices =
    [
        "BarkFluff.Identity",
        "BarkFluff.Users",
        "BarkFluff.Messages",
        "BarkFluff.Files",
        "BarkFluff.Updates",
        "BarkFluff.Notification",
        "BarkFluff.Beacon",
        "BarkFluff.FastAuth",
        "BarkFluff.Onliner",
        "BarkFluff.Configuration"
    ];

    public static void MapSeqEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq")
            .WithName("Seq")
            .WithTags("Seq");

        group.MapGet("/events", async (
            SeqService seqService,
            HttpContext context,
            string? application,
            int count = 50,
            string? fromUtc = null) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var filter = !string.IsNullOrEmpty(application)
                ? $"Application = '{application}'"
                : null;

            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(fromUtc) && DateTime.TryParse(fromUtc, out var parsed))
                fromDate = parsed;

            var events = await seqService.GetEventsAsync(filter, count, fromDate);
            return events.HasValue ? Results.Ok(events.Value) : Results.StatusCode(502);
        })
        .WithName("GetSeqEvents")
        .WithOpenApi();

        group.MapGet("/services", (HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            return Results.Ok(KnownServices);
        })
        .WithName("GetSeqServices")
        .WithOpenApi();
    }
}
