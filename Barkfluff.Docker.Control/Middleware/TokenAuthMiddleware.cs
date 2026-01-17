using Barkfluff.Docker.Control.Services;

namespace Barkfluff.Docker.Control.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TokenService _tokenService;
    private readonly ILogger<TokenAuthMiddleware> _logger;

    private static readonly string[] ProtectedPaths =
    {
        "/Dashboard.html",
        "/dashboard.html"
    };

    private static readonly string[] ApiPrefixes =
    {
        "/api"
    };

    private static readonly string[] ExcludedPaths =
    {
        "/api/auth/request",
        "/api/auth/status"
    };

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

        // Check if path requires authentication
        var requiresAuth = RequiresAuthentication(path);

        if (!requiresAuth)
        {
            await _next(context);
            return;
        }

        // Try to get token from cookie
        if (!context.Request.Cookies.TryGetValue("auth_token", out var tokenValue) ||
            !Guid.TryParse(tokenValue, out var tokenId))
        {
            HandleUnauthorized(context, path);
            return;
        }

        var token = _tokenService.ValidateToken(tokenId);
        if (token == null)
        {
            // Token is invalid or expired
            context.Response.Cookies.Delete("auth_token");
            HandleUnauthorized(context, path);
            return;
        }

        // Store token in context for later use
        context.Items["AuthToken"] = token;

        // Special handling for Login.html with valid token - redirect to dashboard
        if (path.Equals("/Login.html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/login.html", StringComparison.OrdinalIgnoreCase) ||
            path == "/")
        {
            context.Response.Redirect("/Dashboard.html");
            return;
        }

        await _next(context);
    }

    private bool RequiresAuthentication(string path)
    {
        // Exclude auth request and status endpoints
        if (ExcludedPaths.Any(excluded => path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check if it's an API endpoint
        if (ApiPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check if it's a protected page
        return ProtectedPaths.Any(protectedPath =>
            path.Equals(protectedPath, StringComparison.OrdinalIgnoreCase));
    }

    private void HandleUnauthorized(HttpContext context, string path)
    {
        // For API endpoints, return 401
        if (ApiPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // For pages, redirect to login
        context.Response.Redirect("/Login.html");
    }
}

public static class TokenAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TokenAuthMiddleware>();
    }
}
