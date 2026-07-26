using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Middleware;

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
                await NotifyAboutUnexpectedClientAsync(context, token);
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
        var pageToken = ValidateToken(context);
        if (pageToken == null)
        {
            // No valid token - redirect to login.
            // Allow the login page and its static assets (md3.css, sidebar.js) through.
            if (path != "/" &&
                !path.Equals("/Login.html", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
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
        await NotifyAboutUnexpectedClientAsync(context, pageToken);
        await _next(context);
    }

    private async Task NotifyAboutUnexpectedClientAsync(HttpContext context, Barkfluff.AdminPanel.Models.AuthToken token)
    {
        if (!token.ApprovedByTelegramUserId.HasValue)
            return;

        // Old sessions may have been created before the real IP address was available.
        // Without a baseline, reporting a mismatch would be a false positive.
        if (string.IsNullOrWhiteSpace(token.IpAddress) || string.IsNullOrWhiteSpace(token.UserAgent))
            return;

        var currentIpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "неизвестен";
        var currentUserAgent = context.Request.Headers.UserAgent.ToString();
        var isDifferentIpAddress = !string.Equals(token.IpAddress, currentIpAddress, StringComparison.Ordinal);
        var isDifferentUserAgent = !string.Equals(token.UserAgent, currentUserAgent, StringComparison.Ordinal);
        if (!isDifferentIpAddress && !isDifferentUserAgent)
            return;

        var fingerprint = $"{currentIpAddress}|{currentUserAgent}";
        if (!_tokenService.TryRegisterSecurityAlert(token.Id, fingerprint))
            return;

        var telegramBotService = context.RequestServices.GetRequiredService<TelegramBotService>();
        await telegramBotService.SendSessionSecurityAlertAsync(token, currentIpAddress, currentUserAgent, context.RequestAborted);
    }

    private Barkfluff.AdminPanel.Models.AuthToken? ValidateToken(HttpContext context)
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
