using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Services;

public class AdminService
{
    private readonly TokenDbContext _db;
    private readonly IOptions<TelegramSettings> _telegramSettings;
    private readonly ILogger<AdminService> _logger;

    public AdminService(TokenDbContext db, IOptions<TelegramSettings> telegramSettings, ILogger<AdminService> logger)
    {
        _db = db;
        _telegramSettings = telegramSettings;
        _logger = logger;
    }

    /// <summary>
    /// Telegram:Admins defines who is an admin at all; LiteDB stores per-admin roles.
    /// New admins from config get the full role set, removed admins lose their record
    /// (their existing sessions degrade to Viewer).
    /// </summary>
    public void EnsureBootstrapped()
    {
        var configAdmins = _telegramSettings.Value.ParsedAdmins;
        var configIds = configAdmins.Select(a => a.TelegramUserId).ToHashSet();
        var records = _db.Admins.FindAll().ToList();

        foreach (var admin in configAdmins)
        {
            var existing = records.FirstOrDefault(r => r.TelegramUserId == admin.TelegramUserId);
            if (existing == null)
            {
                _db.Admins.Insert(new AdminRecord
                {
                    TelegramUserId = admin.TelegramUserId,
                    Username = admin.Username,
                    Roles = AdminRoles.ToNames(AdminRoles.ActiveRoles),
                    UpdatedBy = "bootstrap"
                });
                _logger.LogInformation("Bootstrapped admin {Username} with full roles", admin.Username);
            }
            else if (!string.Equals(existing.Username, admin.Username, StringComparison.Ordinal))
            {
                existing.Username = admin.Username;
                _db.Admins.Update(existing);
            }
        }

        foreach (var record in records.Where(r => !configIds.Contains(r.TelegramUserId)))
        {
            _db.Admins.Delete(record.TelegramUserId);
            _logger.LogWarning("Removed admin record {Username}: not listed in Telegram:Admins", record.Username);
        }
    }

    public HashSet<AdminRole> GetRoles(long telegramUserId)
    {
        return _db.Admins.FindById(telegramUserId)?.RoleSet ?? new HashSet<AdminRole>();
    }

    public AdminRecord? GetRecord(long telegramUserId)
    {
        return _db.Admins.FindById(telegramUserId);
    }

    public List<AdminRecord> GetAll()
    {
        return _db.Admins.Query().OrderBy(r => r.Username).ToList();
    }

    /// <summary>
    /// Replaces the role set of a config-listed admin.
    /// Rejects changes that would leave the panel without a single SecurityAdmin.
    /// </summary>
    public bool UpdateRoles(long telegramUserId, IEnumerable<AdminRole> roles, string updatedBy)
    {
        if (_telegramSettings.Value.ParsedAdmins.All(a => a.TelegramUserId != telegramUserId))
            return false;

        var record = _db.Admins.FindById(telegramUserId);
        var newRoles = AdminRoles.ToNames(roles);

        var hasSecurityAdminAfter = newRoles.Contains(nameof(AdminRole.SecurityAdmin)) ||
                                   GetAll().Any(r => r.TelegramUserId != telegramUserId && r.RoleSet.Contains(AdminRole.SecurityAdmin));

        if (!hasSecurityAdminAfter)
            return false;

        if (record == null)
        {
            record = new AdminRecord
            {
                TelegramUserId = telegramUserId,
                Username = _telegramSettings.Value.ParsedAdmins.First(a => a.TelegramUserId == telegramUserId).Username
            };
            record.Roles = newRoles;
            record.UpdatedBy = updatedBy;
            _db.Admins.Insert(record);
            return true;
        }

        record.Roles = newRoles;
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = updatedBy;
        return _db.Admins.Update(record);
    }
}
