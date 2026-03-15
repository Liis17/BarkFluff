namespace BarkFluff.ClientStorage.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _uploadToken;

    public TokenAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _uploadToken = configuration["UPLOAD_TOKEN"]
            ?? throw new InvalidOperationException("UPLOAD_TOKEN environment variable is required");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/set"))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(authHeader)
                || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                || authHeader["Bearer ".Length..] != _uploadToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        await _next(context);
    }
}
