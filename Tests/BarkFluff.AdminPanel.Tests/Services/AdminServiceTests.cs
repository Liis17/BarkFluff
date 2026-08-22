using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public sealed class AdminServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-admins-{Guid.NewGuid():N}.db");
    private readonly TokenDbContext _db;
    private readonly TelegramSettings _settings = new();

    private AdminService CreateService()
    {
        return new AdminService(_db, Options.Create(_settings), NullLogger<AdminService>.Instance);
    }

    public AdminServiceTests()
    {
        _db = new TokenDbContext(Options.Create(new LiteDbSettings { Path = _dbPath }));
    }

    private void SetConfigAdmins(params (long id, string username)[] admins)
    {
        _settings.ParsedAdmins = admins
            .Select(a => new AdminUser(a.id, a.username))
            .ToList();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void EnsureBootstrapped_InsertsConfigAdminsWithFullRoles()
    {
        SetConfigAdmins((100, "alice"), (200, "bob"));

        CreateService().EnsureBootstrapped();

        var roles = _db.Admins.FindById(100);
        Assert.NotNull(roles);
        Assert.Equal("alice", roles.Username);
        Assert.Superset(AdminRoles.ParseNames(AdminRoles.ToNames(AdminRoles.ActiveRoles)), roles.RoleSet);
        Assert.Contains(AdminRole.SecurityAdmin, _db.Admins.FindById(200)!.RoleSet);
    }

    [Fact]
    public void EnsureBootstrapped_IsIdempotent_AndKeepsEditedRoles()
    {
        SetConfigAdmins((100, "alice"), (200, "bob"));
        var service = CreateService();
        service.EnsureBootstrapped();
        service.UpdateRoles(100, new[] { AdminRole.Support }, "test");

        service.EnsureBootstrapped();

        var record = _db.Admins.FindById(100);
        Assert.NotNull(record);
        Assert.Equal(AdminRole.Support, Assert.Single(record.RoleSet));
    }

    [Fact]
    public void EnsureBootstrapped_RemovesRecordsNotInConfig()
    {
        SetConfigAdmins((100, "alice"));
        var service = CreateService();
        service.EnsureBootstrapped();

        SetConfigAdmins((100, "alice"), (300, "carol"));
        service.UpdateRoles(300, new[] { AdminRole.Support }, "test");
        SetConfigAdmins((100, "alice"));
        service.EnsureBootstrapped();

        Assert.Null(_db.Admins.FindById(300));
        Assert.NotNull(_db.Admins.FindById(100));
    }

    [Fact]
    public void GetRoles_UnknownAdmin_ReturnsEmpty()
    {
        SetConfigAdmins((100, "alice"));
        CreateService().EnsureBootstrapped();

        var roles = CreateService().GetRoles(999);

        Assert.Empty(roles);
    }

    [Fact]
    public void UpdateRoles_RejectsAdminNotInConfig()
    {
        SetConfigAdmins((100, "alice"));
        var service = CreateService();
        service.EnsureBootstrapped();

        var result = service.UpdateRoles(999, new[] { AdminRole.Support }, "test");

        Assert.False(result);
    }

    [Fact]
    public void UpdateRoles_RejectsRemovingLastSecurityAdmin()
    {
        SetConfigAdmins((100, "alice"));
        var service = CreateService();
        service.EnsureBootstrapped();

        var result = service.UpdateRoles(100, new[] { AdminRole.Support }, "test");

        Assert.False(result);
        Assert.Contains(AdminRole.SecurityAdmin, _db.Admins.FindById(100)!.RoleSet);
    }

    [Fact]
    public void UpdateRoles_AllowsRemovingSecurityAdmin_WhenAnotherExists()
    {
        SetConfigAdmins((100, "alice"), (200, "bob"));
        var service = CreateService();
        service.EnsureBootstrapped();

        var result = service.UpdateRoles(100, new[] { AdminRole.Support }, "bob");

        Assert.True(result);
        var record = _db.Admins.FindById(100);
        Assert.NotNull(record);
        Assert.Equal(AdminRole.Support, Assert.Single(record.RoleSet));
        Assert.Equal("bob", record.UpdatedBy);
    }

    [Fact]
    public void UpdateRoles_ReplacesEntireRoleSet()
    {
        SetConfigAdmins((100, "alice"));
        var service = CreateService();
        service.EnsureBootstrapped();

        service.UpdateRoles(100, new[] { AdminRole.SecurityAdmin, AdminRole.OperationsAdmin, AdminRole.Viewer }, "test");

        var record = _db.Admins.FindById(100);
        Assert.NotNull(record);
        Assert.Equal(2, record.RoleSet.Count);
        Assert.DoesNotContain(nameof(AdminRole.Viewer), record.Roles);
    }
}
