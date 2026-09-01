using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Управление администраторами (доступно SecurityAdmin и Owner).
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
            var admins = adminService.GetAll().Select(record => new
            {
                telegramUserId = record.TelegramUserId,
                username = record.Username,
                isOwner = adminService.IsOwner(record.TelegramUserId),
                roles = adminService.GetRoles(record.TelegramUserId)
                    .Select(AdminRoles.DisplayName)
                    .ToList(),
                avatarUrl = $"/api/admins/{record.TelegramUserId}/avatar?v={record.UpdatedAt.Ticks}",
                updatedAt = record.UpdatedAt,
                updatedBy = record.UpdatedBy
            });

            return Results.Ok(admins);
        })
        .WithName("ListAdmins");

        group.MapPost("/invitations", async (
            [FromBody] AdminInvitationCreateRequest request,
            HttpContext context,
            AdminInvitationService invitationService,
            TelegramBotService telegramBotService,
            AuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var actor = context.GetAuthToken()!;
            var botUsername = await telegramBotService.GetBotUsernameAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(botUsername))
            {
                return Results.Problem(
                    title: "Telegram bot is unavailable",
                    detail: "Не удалось получить username Telegram-бота",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var createdBy = actor.AdminUsername ?? "unknown";
            var result = invitationService.Create(
                request.TelegramUserId,
                request.Username ?? string.Empty,
                createdBy,
                botUsername);

            if (!result.Success)
            {
                var response = new { error = result.ErrorCode, message = result.ErrorMessage };
                return result.ErrorCode is "already_admin" or "owner_target" or "username_in_use"
                    ? Results.Conflict(response)
                    : Results.BadRequest(response);
            }

            var invitation = result.Invitation!;
            auditService.Log(new AuditLogEntry
            {
                AdminUsername = createdBy,
                TelegramUserId = actor.ApprovedByTelegramUserId,
                Action = "admins.invitation.create",
                Details = $"Приглашение для @{invitation.Username} (Telegram ID: {invitation.TelegramUserId})",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                Outcome = "ok"
            });

            return Results.Ok(new
            {
                id = invitation.Id,
                telegramUserId = invitation.TelegramUserId,
                username = invitation.Username,
                status = invitation.Status.ToString().ToLowerInvariant(),
                expiresAt = invitation.ExpiresAt,
                link = result.Link
            });
        })
        .WithName("CreateAdminInvitation")
        .RequireStepUpFromArguments(
            StepUpActions.AdminsInvite,
            context =>
            {
                var request = context.Arguments.OfType<AdminInvitationCreateRequest>().FirstOrDefault();
                return request == null
                    ? "target=unknown"
                    : $"target={request.TelegramUserId};username={AdminService.NormalizeUsername(request.Username ?? string.Empty)}";
            });

        group.MapGet("/invitations/{invitationId:guid}", (Guid invitationId, AdminInvitationService invitationService) =>
        {
            var invitation = invitationService.Get(invitationId);
            if (invitation == null)
                return Results.NotFound(new { message = "Приглашение не найдено" });

            return Results.Ok(new
            {
                id = invitation.Id,
                telegramUserId = invitation.TelegramUserId,
                username = invitation.Username,
                status = invitation.Status.ToString().ToLowerInvariant(),
                expiresAt = invitation.ExpiresAt,
                resolvedAt = invitation.ResolvedAt
            });
        })
        .WithName("GetAdminInvitationStatus");

        group.MapDelete("/{telegramUserId:long}", (
            long telegramUserId,
            HttpContext context,
            AdminService adminService,
            AdminInvitationService invitationService,
            TokenService tokenService,
            AuditService auditService) =>
        {
            var actor = context.GetAuthToken()!;
            var record = adminService.GetRecord(telegramUserId);
            if (record == null || adminService.IsOwner(telegramUserId))
                return Results.Conflict(new { message = "Owner нельзя удалить, а администратор не найден" });

            if (!adminService.DeleteAdmin(telegramUserId))
                return Results.NotFound(new { message = "Администратор не найден" });

            tokenService.DeleteTokensByAdmin(telegramUserId);
            invitationService.InvalidatePendingForTarget(telegramUserId);

            auditService.Log(new AuditLogEntry
            {
                AdminUsername = actor.AdminUsername ?? "unknown",
                TelegramUserId = actor.ApprovedByTelegramUserId,
                Action = "admins.delete",
                Details = $"Удалён @{record.Username} (Telegram ID: {telegramUserId})",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                Outcome = "ok"
            });

            return Results.NoContent();
        })
        .WithName("DeleteAdmin")
        .RequireStepUp(
            StepUpActions.AdminsDelete,
            context => $"target={context.Request.RouteValues["telegramUserId"]}");

        group.MapGet("/{telegramUserId:long}/avatar", async (
            long telegramUserId,
            AdminService adminService,
            TelegramBotService telegramBotService,
            CancellationToken cancellationToken) =>
        {
            if (!adminService.IsActiveAdmin(telegramUserId))
                return Results.NotFound();

            var photo = await telegramBotService.GetProfilePhotoAsync(telegramUserId, cancellationToken);
            return photo == null ? Results.NotFound() : Results.File(photo, "image/jpeg");
        })
        .WithName("GetAdminTelegramAvatar");

        group.MapPost("/{telegramUserId:long}/roles", (
            long telegramUserId,
            [FromBody] AdminRolesUpdateRequest request,
            HttpContext context,
            AdminService adminService,
            AuditService auditService) =>
        {
            var actor = context.GetAuthToken()!;
            var updatedBy = actor.AdminUsername ?? "unknown";
            var previousRoles = adminService.GetRoles(telegramUserId)
                .Select(AdminRoles.DisplayName)
                .ToList();
            var roles = AdminRoles.ParseNames(request.Roles ?? Array.Empty<string>());

            if (!adminService.UpdateRoles(telegramUserId, roles, updatedBy))
            {
                return Results.BadRequest(new
                {
                    message = "Администратор не найден, роль Owner нельзя назначать, а owner нельзя изменять"
                });
            }

            var record = adminService.GetRecord(telegramUserId)!;
            var currentRoles = adminService.GetRoles(telegramUserId)
                .Select(AdminRoles.DisplayName)
                .ToList();

            auditService.Log(new AuditLogEntry
            {
                AdminUsername = updatedBy,
                TelegramUserId = actor.ApprovedByTelegramUserId,
                Action = "admins.roles.update",
                Details = $"{record.Username}: [{string.Join(", ", previousRoles)}] → [{string.Join(", ", currentRoles)}]",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                Outcome = "ok"
            });

            return Results.Ok(new
            {
                telegramUserId,
                username = record.Username,
                isOwner = adminService.IsOwner(telegramUserId),
                roles = currentRoles
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

public class AdminInvitationCreateRequest
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
}
