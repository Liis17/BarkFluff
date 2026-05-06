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

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/set"))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(authHeader)
                || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            var providedBytes = Encoding.UTF8.GetBytes(authHeader["Bearer ".Length..]);

            if (!CryptographicOperations.FixedTimeEquals(providedBytes, _uploadTokenBytes))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        await _next(context);
    }
}
