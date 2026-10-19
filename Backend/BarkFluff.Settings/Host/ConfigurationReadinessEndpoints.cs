using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Settings.Host;

public static class ConfigurationReadinessEndpoints
{
    public static IEndpointRouteBuilder MapConfigurationReadiness(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/internal/configuration/ready", async (
            SettingsContext context,
            SettingsReadinessContributor readiness,
            CancellationToken cancellationToken,
            bool requireValidSettings = true) =>
        {
            try
            {
                // Query the actual settings tables, not just the database connection.
                foreach (var scope in SettingsScopes.All)
                    await context.Settings(scope).AnyAsync(cancellationToken);

                if (requireValidSettings && (await readiness.CheckAsync(cancellationToken)).Status != "healthy")
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                return Results.Ok();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        return endpoints;
    }
}
