using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Middleware;

public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequirePermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var token = http.Items["AuthToken"] as AuthToken;
        if (token == null)
            return Results.Unauthorized();

        if (!http.HasPermission(_permission))
            return Results.Json(
                new { error = "forbidden", permission = _permission },
                statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}

public static class RequirePermissionExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(new RequirePermissionFilter(permission));
    }

    public static bool HasPermission(this HttpContext http, string permission)
    {
        var token = http.Items["AuthToken"] as AuthToken;
        if (token?.ApprovedByTelegramUserId is not long telegramUserId)
            return AdminPermissions.IsAllowed(permission, new HashSet<AdminRole>());

        var adminService = http.RequestServices.GetRequiredService<AdminService>();
        return AdminPermissions.IsAllowed(permission, adminService.GetRoles(telegramUserId));
    }

    public static AuthToken? GetAuthToken(this HttpContext http)
    {
        return http.Items["AuthToken"] as AuthToken;
    }
}
