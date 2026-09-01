using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public sealed class AdminInvitationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-invitations-{Guid.NewGuid():N}.db");
    private readonly TokenDbContext _db;
    private readonly TelegramSettings _settings = new()
    {
        ParsedAdmins = [new AdminUser(100, "alice")]
    };
    private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly AdminService _adminService;
    private readonly AdminInvitationService _invitationService;

    public AdminInvitationServiceTests()
    {
        _db = new TokenDbContext(Options.Create(new LiteDbSettings { Path = _dbPath }));
        _adminService = new AdminService(_db, Options.Create(_settings), NullLogger<AdminService>.Instance);
        _adminService.EnsureBootstrapped();
        _invitationService = new AdminInvitationService(
            _db,
            _adminService,
            NullLogger<AdminInvitationService>.Instance,
            () => _now);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void Create_PersistsInvitationAndBuildsDeepLink()
    {
        var result = _invitationService.Create(200, "@bobuser", "alice", "@barkbot");

        Assert.True(result.Success);
        Assert.NotNull(result.Invitation);
        Assert.Equal(AdminInvitationStatus.Pending, result.Invitation!.Status);
        Assert.Equal(_now.AddMinutes(10), result.Invitation.ExpiresAt);
        Assert.StartsWith("https://t.me/barkbot?start=", result.Link);
        Assert.Equal(result.Invitation.Id, _invitationService.Get(result.Invitation.Id)!.Id);
    }

    [Fact]
    public void Accept_RequiresMatchingTelegramIdentity_AndCreatesViewerAdmin()
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        var wrongUser = _invitationService.Accept(invitation.Payload, 201, "bobuser");
        Assert.Equal(AdminInvitationActionStatus.IdentityMismatch, wrongUser.Status);
        Assert.Null(_adminService.GetRecord(200));

        var accepted = _invitationService.Accept(invitation.Payload, 200, "@BOBUSER");
        Assert.Equal(AdminInvitationActionStatus.Accepted, accepted.Status);
        Assert.Empty(_adminService.GetRoles(200));
        Assert.Equal(AdminInvitationStatus.Accepted, _invitationService.Get(invitation.Id)!.Status);

        var repeated = _invitationService.Accept(invitation.Payload, 200, "bobuser");
        Assert.Equal(AdminInvitationActionStatus.AlreadyResolved, repeated.Status);
    }

    [Fact]
    public void Reject_RequiresMatchingUsername_AndDoesNotCreateAdmin()
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        var mismatch = _invitationService.Reject(invitation.Payload, 200, "otheruser");
        Assert.Equal(AdminInvitationActionStatus.IdentityMismatch, mismatch.Status);

        var rejected = _invitationService.Reject(invitation.Payload, 200, "bobuser");
        Assert.Equal(AdminInvitationActionStatus.Rejected, rejected.Status);
        Assert.Null(_adminService.GetRecord(200));
    }

    [Fact]
    public void ExpiredInvitationCannotBeAccepted()
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;
        _now = _now.AddMinutes(10);

        Assert.Equal(AdminInvitationStatus.Expired, _invitationService.Get(invitation.Id)!.Status);
        var result = _invitationService.Accept(invitation.Payload, 200, "bobuser");
        Assert.Equal(AdminInvitationActionStatus.Expired, result.Status);
        Assert.Null(_adminService.GetRecord(200));
    }

    [Fact]
    public void NewInvitationExpiresPreviousPendingInvitation()
    {
        var first = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;
        var second = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        Assert.Equal(AdminInvitationStatus.Expired, _invitationService.Get(first.Id)!.Status);
        Assert.Equal(AdminInvitationStatus.Pending, _invitationService.Get(second.Id)!.Status);
    }

    [Fact]
    public void Create_RejectsOwnerAndExistingUsername()
    {
        var owner = _invitationService.Create(100, "alice", "alice", "barkbot");
        Assert.Equal("owner_target", owner.ErrorCode);

        var duplicate = _invitationService.Create(200, "alice", "alice", "barkbot");
        Assert.Equal("username_in_use", duplicate.ErrorCode);
    }
}
