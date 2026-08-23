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
            AdminService adminService,
            AuditService auditService) =>
        {
            var roles = AdminRoles.ParseNames(request.Roles ?? Array.Empty<string>());
            var actor = context.GetAuthToken()!;
            var updatedBy = actor.AdminUsername ?? "unknown";
            var previousRoles = adminService.GetRecord(telegramUserId)?.RoleSet.Select(AdminRoles.DisplayName).ToList() ?? new List<string>();

            if (!adminService.UpdateRoles(telegramUserId, roles, updatedBy))
                return Results.BadRequest(new { message = "Админ не найден в Telegram:Admins или изменение оставит панель без SecurityAdmin" });

            var record = adminService.GetRecord(telegramUserId)!;

            auditService.Log(new AuditLogEntry
            {
                AdminUsername = updatedBy,
                TelegramUserId = actor.ApprovedByTelegramUserId,
                Action = "admins.roles.update",
                Details = $"{record.Username}: [{string.Join(", ", previousRoles)}] → [{string.Join(", ", record.RoleSet.Select(AdminRoles.DisplayName))}]",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                Outcome = "ok"
            });

            return Results.Ok(new
            {
                telegramUserId,
                username = record.Username,
                roles = record.RoleSet.Select(AdminRoles.DisplayName).ToList()
            });
        })
        .WithName("UpdateAdminRoles")
        .RequireStepUpFromArguments(
            StepUpActions.AdminsRolesUpdate,
            context =>
            {
                var target = context.HttpContext.Request.RouteValues["telegramUserId"];
                var request = context.Arguments.OfType<AdminRolesUpdateRequest>().FirstOrDefault();
                var roles = request?.Roles is { } requestedRoles
                    ? string.Join(",", requestedRoles.Order(StringComparer.OrdinalIgnoreCase))
                    : string.Empty;
                return $"target={target};roles={roles}";
            });
    }
}

public class AdminRolesUpdateRequest
{
    public string[]? Roles { get; set; }
}
