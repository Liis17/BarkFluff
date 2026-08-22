using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Append-only audit log of critical admin actions.
/// </summary>
public class AuditService
{
    public const int RetentionDays = 90;

    private readonly AuditDbContext _db;
    private readonly ILogger<AuditService> _logger;
    private readonly Timer _cleanupTimer;

    public AuditService(AuditDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromHours(6), TimeSpan.FromHours(24));
    }

    public void Log(AuditLogEntry entry)
    {
        try
        {
            _db.Entries.Insert(entry);
        }
        catch (Exception ex)
        {
            // Аудит не должен ломать выполнение самого действия
            _logger.LogError(ex, "Failed to write audit entry for {Action}", entry.Action);
        }
    }

    public List<AuditLogEntry> GetEntries(int limit, DateTime? beforeUtc)
    {
        var query = _db.Entries.Query().OrderByDescending(x => x.At);
        if (beforeUtc.HasValue)
        {
            var cutoff = beforeUtc.Value;
            return query.Where(x => x.At < cutoff).Limit(limit).ToList();
        }

        return query.Limit(limit).ToList();
    }

    private void Cleanup()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            _db.Entries.DeleteMany(x => x.At < cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log cleanup failed");
        }
    }
}
