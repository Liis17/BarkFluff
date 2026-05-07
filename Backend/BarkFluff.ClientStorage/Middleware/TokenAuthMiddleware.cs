using BarkFluff.ClientStorage.Infrastructure;

using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.ClientStorage.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _uploadTokenBytes;

    public TokenAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var token = configuration["UPLOAD_TOKEN"]
            ?? throw new InvalidOperationException("UPLOAD_TOKEN environment variable is required");
        _uploadTokenBytes = Encoding.UTF8.GetBytes(token);
    }

    public async Task InvokeAsync(HttpContext context, MetricsCollector metrics)
    {
        if (context.Request.Path.StartsWithSegments("/set"))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(authHeader)
                || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                metrics.Increment("auth_unauthorized_total");
                metrics.Increment("auth_missing_token_total");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            var providedBytes = Encoding.UTF8.GetBytes(authHeader["Bearer ".Length..]);

            if (!CryptographicOperations.FixedTimeEquals(providedBytes, _uploadTokenBytes))
            {
                metrics.Increment("auth_unauthorized_total");
                metrics.Increment("auth_invalid_token_total");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            metrics.Increment("auth_success_total");
        }

        await _next(context);
    }
}
