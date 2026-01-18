using Barkfluff.Docker.Control.Services;

namespace Barkfluff.Docker.Control.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TokenService _tokenService;
    private readonly ILogger<TokenAuthMiddleware> _logger;

    public TokenAuthMiddleware(
        RequestDelegate next,
        TokenService tokenService,
        ILogger<TokenAuthMiddleware> logger)
    {
        _next = next;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // API endpoints
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            // Validate token for all API endpoints
            var token = ValidateToken(context);
            if (token != null)
            {
                context.Items["AuthToken"] = token;
            }

            // Allow unauthenticated access to public auth endpoints
            if (path.StartsWith("/api/auth/request", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/auth/status", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // All other API endpoints require authentication
            if (token == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
            return;
        }

        // HTML pages routing - validate token and store in context
        // Actual file serving is handled by PageRoutingMiddleware
        var pageToken = ValidateToken(context);
        if (pageToken == null)
        {
            // No valid token - redirect to login
            if (path != "/" && !path.Equals("/Login.html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/");
                return;
            }
            // Let Login.html be served
            await _next(context);
            return;
        }

        // Valid token - store in context and continue
        context.Items["AuthToken"] = pageToken;
        await _next(context);
    }

    private Barkfluff.Docker.Control.Models.AuthToken? ValidateToken(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue("auth_token", out var tokenValue) ||
            !Guid.TryParse(tokenValue, out var tokenId))
        {
            return null;
        }

        var token = _tokenService.ValidateToken(tokenId);
        if (token == null)
        {
            // Token is invalid or expired - delete cookie
            context.Response.Cookies.Delete("auth_token");
            return null;
        }

        return token;
    }
}

public static class TokenAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TokenAuthMiddleware>();
    }
}
