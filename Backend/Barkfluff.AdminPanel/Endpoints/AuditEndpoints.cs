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

        // GET /api/audit?limit=100&beforeUtc=...&admin=&action=&outcome=&fromUtc=&toUtc= — записи новые сверху.
        // Фильтры в AuditService применяются до limit; beforeUtc — курсор «Загрузить ещё» внутри той же выборки.
        group.MapGet("/", (
            AuditService auditService,
            int? limit,
            DateTime? beforeUtc,
            string? admin,
            string? action,
            string? outcome,
            DateTime? fromUtc,
            DateTime? toUtc) =>
        {
            var filter = new AuditEntryFilter(admin, action, outcome, fromUtc, toUtc);
            var validationError = AuditService.ValidateFilter(filter);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var entries = auditService.GetEntries(Math.Clamp(limit ?? 100, 1, 500), beforeUtc, filter);

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

        // GET /api/audit/facets — подсказки для фильтров: администраторы и действия из сохранённой истории
        group.MapGet("/facets", (AuditService auditService) =>
        {
            var (admins, actions) = auditService.GetFacets();
            return Results.Ok(new { admins, actions });
        })
        .WithName("GetAuditFacets");
    }
}
