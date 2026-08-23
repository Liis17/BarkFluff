using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Аудит-лог критических действий (доступен SecurityAdmin).
/// </summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit")
            .WithTags("Audit")
            .RequirePermission(AdminPermissions.AuditRead);

        // GET /api/audit?limit=100&beforeUtc=... — последние записи (новые сверху)
        group.MapGet("/", (
            AuditService auditService,
            int? limit,
            DateTime? beforeUtc) =>
        {
            var entries = auditService.GetEntries(Math.Clamp(limit ?? 100, 1, 500), beforeUtc);

            return Results.Ok(entries.Select(e => new
            {
                id = e.Id.ToString(),
                at = e.At,
                adminUsername = e.AdminUsername,
                telegramUserId = e.TelegramUserId,
                action = e.Action,
                details = e.Details,
                ipAddress = e.IpAddress,
                confirmationId = e.ConfirmationId,
                outcome = e.Outcome
            }));
        })
        .WithName("ListAuditEntries");
    }
}
