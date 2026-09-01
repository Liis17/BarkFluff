using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Services;

public enum AdminInvitationActionStatus
{
    Accepted,
    Rejected,
    Expired,
    NotFound,
    IdentityMismatch,
    AlreadyResolved,
    Conflict
}

public sealed class AdminInvitationCreateResult
{
    public AdminInvitation? Invitation { get; init; }
    public string? Link { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Success => Invitation != null && Link != null;

    public static AdminInvitationCreateResult Ok(AdminInvitation invitation, string link) => new()
    {
        Invitation = invitation,
        Link = link
    };

    public static AdminInvitationCreateResult Fail(string code, string message) => new()
    {
        ErrorCode = code,
        ErrorMessage = message
    };
}

public sealed class AdminInvitationActionResult
{
    public AdminInvitationActionStatus Status { get; init; }
    public AdminInvitation? Invitation { get; init; }
}

/// <summary>
/// Persistent, single-use Telegram deep-link invitations for administrators.
/// </summary>
public class AdminInvitationService
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(10);

    private readonly TokenDbContext _db;
    private readonly AdminService _adminService;
    private readonly ILogger<AdminInvitationService> _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly object _stateLock = new();

    public AdminInvitationService(
        TokenDbContext db,
        AdminService adminService,
        ILogger<AdminInvitationService> logger,
        Func<DateTime>? utcNow = null)
    {
        _db = db;
        _adminService = adminService;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public AdminInvitationCreateResult Create(
        long telegramUserId,
        string username,
        string createdBy,
        string botUsername)
    {
        if (telegramUserId <= 0)
            return AdminInvitationCreateResult.Fail("invalid_telegram_id", "Telegram ID должен быть положительным числом");

        var normalizedUsername = AdminService.NormalizeUsername(username);
        if (!AdminService.IsValidUsername(normalizedUsername))
            return AdminInvitationCreateResult.Fail("invalid_username", "Введите корректный Telegram username");

        var normalizedBotUsername = AdminService.NormalizeUsername(botUsername);
        if (!AdminService.IsValidUsername(normalizedBotUsername))
            return AdminInvitationCreateResult.Fail("bot_username_unavailable", "Не удалось получить username Telegram-бота");

        if (_adminService.IsOwner(telegramUserId))
            return AdminInvitationCreateResult.Fail("owner_target", "Owner уже является администратором");

        if (_adminService.IsActiveAdmin(telegramUserId))
            return AdminInvitationCreateResult.Fail("already_admin", "Этот пользователь уже является администратором");

        if (_adminService.GetByUsername(normalizedUsername) != null)
            return AdminInvitationCreateResult.Fail("username_in_use", "Этот username уже используется другим администратором");

        lock (_stateLock)
        {
            var now = _utcNow();
            foreach (var previous in _db.AdminInvitations
                         .Find(x => x.TelegramUserId == telegramUserId && x.Status == AdminInvitationStatus.Pending)
                         .ToList())
            {
                previous.Status = AdminInvitationStatus.Expired;
                previous.ResolvedAt = now;
                _db.AdminInvitations.Update(previous);
            }

            var invitation = new AdminInvitation
            {
                TelegramUserId = telegramUserId,
                Username = normalizedUsername,
                CreatedBy = createdBy,
                CreatedAt = now,
                ExpiresAt = now.Add(InvitationLifetime),
                Status = AdminInvitationStatus.Pending
            };

            _db.AdminInvitations.Insert(invitation);
            _logger.LogInformation(
                "Created admin invitation {InvitationId} for {TelegramUserId} ({Username})",
                invitation.Id,
                telegramUserId,
                normalizedUsername);

            return AdminInvitationCreateResult.Ok(
                invitation,
                $"https://t.me/{normalizedBotUsername}?start={invitation.Payload}");
        }
    }

    public AdminInvitation? Get(Guid invitationId)
    {
        lock (_stateLock)
        {
            var invitation = _db.AdminInvitations.FindById(invitationId);
            ExpireIfNeeded(invitation);
            return invitation;
        }
    }

    public AdminInvitation? GetByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var normalizedToken = token.Trim();
        lock (_stateLock)
        {
            var invitation = _db.AdminInvitations.FindOne(x => x.Payload == normalizedToken);
            ExpireIfNeeded(invitation);
            return invitation;
        }
    }

    public AdminInvitationActionResult Accept(string token, long telegramUserId, string? username)
    {
        return Resolve(token, telegramUserId, username, AdminInvitationStatus.Accepted);
    }

    public AdminInvitationActionResult Reject(string token, long telegramUserId, string? username)
    {
        return Resolve(token, telegramUserId, username, AdminInvitationStatus.Rejected);
    }

    public void InvalidatePendingForTarget(long telegramUserId)
    {
        lock (_stateLock)
        {
            var now = _utcNow();
            foreach (var invitation in _db.AdminInvitations
                         .Find(x => x.TelegramUserId == telegramUserId && x.Status == AdminInvitationStatus.Pending)
                         .ToList())
            {
                invitation.Status = AdminInvitationStatus.Expired;
                invitation.ResolvedAt = now;
                _db.AdminInvitations.Update(invitation);
            }
        }
    }

    private AdminInvitationActionResult Resolve(
        string token,
        long telegramUserId,
        string? username,
        AdminInvitationStatus resolvedStatus)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new AdminInvitationActionResult { Status = AdminInvitationActionStatus.NotFound };

        var normalizedToken = token.Trim();
        lock (_stateLock)
        {
            var invitation = _db.AdminInvitations.FindOne(x => x.Payload == normalizedToken);
            if (invitation == null)
                return new AdminInvitationActionResult { Status = AdminInvitationActionStatus.NotFound };

            if (invitation.Status != AdminInvitationStatus.Pending)
            {
                return new AdminInvitationActionResult
                {
                    Status = invitation.Status == AdminInvitationStatus.Expired
                        ? AdminInvitationActionStatus.Expired
                        : AdminInvitationActionStatus.AlreadyResolved,
                    Invitation = invitation
                };
            }

            if (_utcNow() >= invitation.ExpiresAt.ToUniversalTime())
            {
                invitation.Status = AdminInvitationStatus.Expired;
                invitation.ResolvedAt = _utcNow();
                _db.AdminInvitations.Update(invitation);
                return new AdminInvitationActionResult
                {
                    Status = AdminInvitationActionStatus.Expired,
                    Invitation = invitation
                };
            }

            var normalizedUsername = AdminService.NormalizeUsername(username ?? string.Empty);
            if (invitation.TelegramUserId != telegramUserId ||
                !string.Equals(invitation.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
            {
                return new AdminInvitationActionResult
                {
                    Status = AdminInvitationActionStatus.IdentityMismatch,
                    Invitation = invitation
                };
            }

            var previousStatus = invitation.Status;
            var previousResolvedAt = invitation.ResolvedAt;
            var previousResolvedBy = invitation.ResolvedByTelegramUserId;
            var persisted = _db.RunInTransaction(() =>
            {
                if (resolvedStatus == AdminInvitationStatus.Accepted &&
                    !_adminService.AddAcceptedAdmin(telegramUserId, normalizedUsername, "telegram invitation"))
                {
                    return false;
                }

                invitation.Status = resolvedStatus;
                invitation.ResolvedAt = _utcNow();
                invitation.ResolvedByTelegramUserId = telegramUserId;
                return _db.AdminInvitations.Update(invitation);
            });

            if (!persisted)
            {
                invitation.Status = previousStatus;
                invitation.ResolvedAt = previousResolvedAt;
                invitation.ResolvedByTelegramUserId = previousResolvedBy;
                return new AdminInvitationActionResult
                {
                    Status = AdminInvitationActionStatus.Conflict,
                    Invitation = invitation
                };
            }

            _logger.LogInformation(
                "Admin invitation {InvitationId} resolved as {Status} by {TelegramUserId}",
                invitation.Id,
                resolvedStatus,
                telegramUserId);

            return new AdminInvitationActionResult
            {
                Status = resolvedStatus == AdminInvitationStatus.Accepted
                    ? AdminInvitationActionStatus.Accepted
                    : AdminInvitationActionStatus.Rejected,
                Invitation = invitation
            };
        }
    }

    private void ExpireIfNeeded(AdminInvitation? invitation)
    {
        if (invitation?.Status != AdminInvitationStatus.Pending || _utcNow() < invitation.ExpiresAt.ToUniversalTime())
            return;

        invitation.Status = AdminInvitationStatus.Expired;
        invitation.ResolvedAt = _utcNow();
        _db.AdminInvitations.Update(invitation);
    }
}
