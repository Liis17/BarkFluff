using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Управление ролями администраторов (доступно SecurityAdmin, с step-up подтверждением).
/// </summary>
public static class AdminsEndpoints
{
    public static void MapAdminsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admins")
            .WithTags("Admins")
            .RequirePermission(AdminPermissions.AdminsRoles);

        group.MapGet("/", (AdminService adminService) =>
        {
            var admins = adminService.GetAll().Select(r => new
            {
                telegramUserId = r.TelegramUserId,
                username = r.Username,
                roles = r.RoleSet.Select(AdminRoles.DisplayName).ToList(),
                updatedAt = r.UpdatedAt,
                updatedBy = r.UpdatedBy
            });

            return Results.Ok(admins);
        })
        .WithName("ListAdmins");

        group.MapPost("/{telegramUserId:long}/roles", (
            long telegramUserId,
            [FromBody] AdminRolesUpdateRequest request,
            HttpContext context,
            AdminService adminService) =>
        {
            var roles = AdminRoles.ParseNames(request.Roles ?? Array.Empty<string>());
            var updatedBy = context.GetAuthToken()!.AdminUsername ?? "unknown";

            if (!adminService.UpdateRoles(telegramUserId, roles, updatedBy))
                return Results.BadRequest(new { message = "Админ не найден в Telegram:Admins или изменение оставит панель без SecurityAdmin" });

            var record = adminService.GetRecord(telegramUserId)!;
            return Results.Ok(new
            {
                telegramUserId,
                username = record.Username,
                roles = record.RoleSet.Select(AdminRoles.DisplayName).ToList()
            });
        })
        .WithName("UpdateAdminRoles")
        .RequireStepUp(
            StepUpActions.AdminsRolesUpdate,
            context => $"target={context.Request.RouteValues["telegramUserId"]}");
    }
}

public class AdminRolesUpdateRequest
{
    public string[]? Roles { get; set; }
}
