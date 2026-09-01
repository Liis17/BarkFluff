using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

using System.Text.RegularExpressions;

namespace Barkfluff.AdminPanel.Services;

public class AdminService
{
    private readonly TokenDbContext _db;
    private readonly IOptions<TelegramSettings> _telegramSettings;
    private readonly ILogger<AdminService> _logger;
    private readonly object _stateLock = new();

    public AdminService(TokenDbContext db, IOptions<TelegramSettings> telegramSettings, ILogger<AdminService> logger)
    {
        _db = db;
        _telegramSettings = telegramSettings;
        _logger = logger;
    }

    public long OwnerTelegramUserId => GetOwnerConfig().TelegramUserId;

    public bool IsOwner(long telegramUserId) => telegramUserId == OwnerTelegramUserId;

    /// <summary>
    /// Telegram:Admins contains exactly one bootstrap identity: the immutable owner.
    /// LiteDB stores all accepted administrators and their editable roles.
    /// </summary>
    public void EnsureBootstrapped()
    {
        lock (_stateLock)
        {
            var owner = GetOwnerConfig();
            var records = _db.Admins.FindAll().ToList();
            var ownerRecord = records.FirstOrDefault(r => r.TelegramUserId == owner.TelegramUserId);

            if (ownerRecord == null)
            {
                _db.Admins.Insert(new AdminRecord
                {
                    TelegramUserId = owner.TelegramUserId,
                    Username = NormalizeUsername(owner.Username),
                    Roles = AdminRoles.ToNames(new[] { AdminRole.Owner }),
                    UpdatedBy = "bootstrap"
                });
                _logger.LogInformation("Bootstrapped owner {Username}", owner.Username);
            }
            else
            {
                var normalizedUsername = NormalizeUsername(owner.Username);
                var roles = AdminRoles.ToNames(new[] { AdminRole.Owner });
                if (!string.Equals(ownerRecord.Username, normalizedUsername, StringComparison.Ordinal) ||
                    !ownerRecord.RoleSet.SetEquals(new[] { AdminRole.Owner }))
                {
                    ownerRecord.Username = normalizedUsername;
                    ownerRecord.Roles = roles;
                    ownerRecord.UpdatedAt = DateTime.UtcNow;
                    ownerRecord.UpdatedBy = "bootstrap";
                    _db.Admins.Update(ownerRecord);
                }
            }

            // Owner is a configuration identity. A stale Owner role on another
            // record must never grant full access after an owner change.
            foreach (var record in records.Where(r => r.TelegramUserId != owner.TelegramUserId))
            {
                if (!record.RoleSet.Contains(AdminRole.Owner))
                    continue;

                record.Roles = AdminRoles.ToNames(record.RoleSet.Where(role => role != AdminRole.Owner));
                record.UpdatedAt = DateTime.UtcNow;
                record.UpdatedBy = "bootstrap";
                _db.Admins.Update(record);
            }
        }
    }

    public HashSet<AdminRole> GetRoles(long telegramUserId)
    {
        if (IsOwner(telegramUserId))
            return new HashSet<AdminRole> { AdminRole.Owner };

        var roles = _db.Admins.FindById(telegramUserId)?.RoleSet ?? new HashSet<AdminRole>();
        roles.Remove(AdminRole.Owner);
        return roles;
    }

    public AdminRecord? GetRecord(long telegramUserId)
    {
        return _db.Admins.FindById(telegramUserId);
    }

    public AdminRecord? GetByUsername(string username)
    {
        var normalized = NormalizeUsername(username);
        return GetAll().FirstOrDefault(record =>
            string.Equals(record.Username, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsActiveAdmin(long telegramUserId)
    {
        return IsOwner(telegramUserId) || GetRecord(telegramUserId) != null;
    }

    public List<AdminRecord> GetAll()
    {
        return _db.Admins.Query()
            .ToList()
            .OrderByDescending(r => r.TelegramUserId == OwnerTelegramUserId)
            .ThenBy(r => r.Username)
            .ToList();
    }

    /// <summary>
    /// Adds a Telegram user after they accepted an invitation. New admins start
    /// without editable roles, which is the Viewer baseline.
    /// </summary>
    public bool AddAcceptedAdmin(long telegramUserId, string username, string updatedBy)
    {
        if (telegramUserId <= 0 || IsOwner(telegramUserId))
            return false;

        var normalizedUsername = NormalizeUsername(username);
        if (!IsValidUsername(normalizedUsername))
            return false;

        lock (_stateLock)
        {
            if (_db.Admins.FindById(telegramUserId) != null || GetByUsername(normalizedUsername) != null)
                return false;

            _db.Admins.Insert(new AdminRecord
            {
                TelegramUserId = telegramUserId,
                Username = normalizedUsername,
                Roles = new List<string>(),
                UpdatedBy = updatedBy
            });
            return true;
        }
    }

    /// <summary>
    /// Replaces the role set of an accepted non-owner administrator.
    /// Owner is intentionally immutable and cannot be assigned to anyone else.
    /// </summary>
    public bool UpdateRoles(long telegramUserId, IEnumerable<AdminRole> roles, string updatedBy)
    {
        if (IsOwner(telegramUserId))
            return false;

        var requestedRoles = roles?.ToHashSet() ?? new HashSet<AdminRole>();
        if (requestedRoles.Contains(AdminRole.Owner))
            return false;

        var record = _db.Admins.FindById(telegramUserId);
        if (record == null)
            return false;

        record.Roles = AdminRoles.ToNames(requestedRoles);
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = updatedBy;
        return _db.Admins.Update(record);
    }

    public bool DeleteAdmin(long telegramUserId)
    {
        if (IsOwner(telegramUserId))
            return false;

        return _db.Admins.Delete(telegramUserId);
    }

    public static string NormalizeUsername(string username)
    {
        return username.Trim().TrimStart('@');
    }

    public static bool IsValidUsername(string username)
    {
        return Regex.IsMatch(username, "^[A-Za-z0-9_]{5,32}$", RegexOptions.CultureInvariant);
    }

    private AdminUser GetOwnerConfig()
    {
        var admins = _telegramSettings.Value.ParsedAdmins;
        if (admins.Count != 1)
        {
            throw new InvalidOperationException(
                "Exactly one Telegram:Admins entry is required; it is the immutable owner identity.");
        }

        return admins[0];
    }
}
