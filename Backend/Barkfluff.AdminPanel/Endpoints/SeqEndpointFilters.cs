using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Routing;

namespace Barkfluff.AdminPanel.Endpoints;

internal static class SeqEndpointFilters
{
    public static RouteGroupBuilder RequireSeqEnabled(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            var seq = context.HttpContext.RequestServices.GetRequiredService<SeqService>();
            var path = context.HttpContext.Request.Path.Value;
            if (string.Equals(path, "/api/seq/services", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/seq/services/status", StringComparison.OrdinalIgnoreCase))
                return await next(context);

            if (!seq.IsEnabled)
                return Results.Json(
                    new { available = false, message = SeqService.DisabledMessage },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            return await next(context);
        });
}
