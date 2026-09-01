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

    public AdminServiceTests()
    {
        _db = new TokenDbContext(Options.Create(new LiteDbSettings { Path = _dbPath }));
    }

    private AdminService CreateService()
    {
        return new AdminService(_db, Options.Create(_settings), NullLogger<AdminService>.Instance);
    }

    private void SetOwner(long id = 100, string username = "alice")
    {
        _settings.ParsedAdmins = [new AdminUser(id, username)];
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void EnsureBootstrapped_InsertsSingleOwnerWithOnlyOwnerRole()
    {
        SetOwner();

        var service = CreateService();
        service.EnsureBootstrapped();

        var record = _db.Admins.FindById(100);
        Assert.NotNull(record);
        Assert.Equal("alice", record.Username);
        Assert.Equal([AdminRole.Owner], record.RoleSet);
        Assert.True(service.IsOwner(100));
        Assert.Equal([AdminRole.Owner], service.GetRoles(100));
    }

    [Fact]
    public void EnsureBootstrapped_IsIdempotent_AndKeepsDynamicAdmins()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();
        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));
        Assert.True(service.UpdateRoles(200, [AdminRole.Support], "alice"));

        service.EnsureBootstrapped();

        var owner = _db.Admins.FindById(100);
        var dynamicAdmin = _db.Admins.FindById(200);
        Assert.NotNull(owner);
        Assert.Equal([AdminRole.Owner], owner.RoleSet);
        Assert.NotNull(dynamicAdmin);
        Assert.Equal([AdminRole.Support], dynamicAdmin.RoleSet);
    }

    [Fact]
    public void EnsureBootstrapped_RestoresOwnerRole_AndStripsOwnerFromOtherRecords()
    {
        SetOwner();
        _db.Admins.Insert(new AdminRecord
        {
            TelegramUserId = 100,
            Username = "alice",
            Roles = [nameof(AdminRole.SecurityAdmin)]
        });
        _db.Admins.Insert(new AdminRecord
        {
            TelegramUserId = 200,
            Username = "bobuser",
            Roles = [nameof(AdminRole.Owner), nameof(AdminRole.Support)]
        });

        var service = CreateService();
        service.EnsureBootstrapped();

        Assert.Equal([AdminRole.Owner], _db.Admins.FindById(100)!.RoleSet);
        Assert.Equal([AdminRole.Support], _db.Admins.FindById(200)!.RoleSet);
    }

    [Fact]
    public void EnsureBootstrapped_RequiresExactlyOneConfiguredOwner()
    {
        _settings.ParsedAdmins = [new AdminUser(100, "alice"), new AdminUser(200, "bobuser")];

        Assert.Throws<InvalidOperationException>(() => CreateService().EnsureBootstrapped());
    }

    [Fact]
    public void Owner_CannotBeChangedOrDeleted()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();

        Assert.False(service.UpdateRoles(100, [AdminRole.Support], "alice"));
        Assert.False(service.DeleteAdmin(100));
        Assert.NotNull(service.GetRecord(100));
        Assert.Equal([AdminRole.Owner], service.GetRoles(100));
    }

    [Fact]
    public void DynamicAdmin_StartsAsViewer_AndCanReceiveEditableRoles()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();

        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));
        Assert.Empty(service.GetRoles(200));
        Assert.True(service.UpdateRoles(200, [AdminRole.Support, AdminRole.Viewer], "alice"));
        Assert.Equal([AdminRole.Support], service.GetRoles(200));
    }

    [Fact]
    public void UpdateRoles_RejectsAssigningOwner()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();
        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));

        Assert.False(service.UpdateRoles(200, [AdminRole.Owner], "alice"));
        Assert.Empty(service.GetRoles(200));
    }

    [Fact]
    public void UpdateRoles_AllowsRemovingLastSecurityAdmin()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();
        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));
        Assert.True(service.UpdateRoles(200, [AdminRole.SecurityAdmin], "alice"));

        Assert.True(service.UpdateRoles(200, Array.Empty<AdminRole>(), "alice"));
        Assert.Empty(service.GetRoles(200));
    }

    [Fact]
    public void DeleteAdmin_RemovesDynamicRecord()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();
        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));

        Assert.True(service.DeleteAdmin(200));
        Assert.Null(service.GetRecord(200));
        Assert.NotNull(service.GetRecord(100));
    }

    [Fact]
    public void DeleteTokensByAdmin_RevokesAllDynamicAdminSessions()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();
        Assert.True(service.AddAcceptedAdmin(200, "bobuser", "telegram invitation"));

        var tokenService = new TokenService(_db, Options.Create(new AuthSettings()));
        var first = tokenService.CreateToken(null, null, "one", "bobuser", 200);
        var second = tokenService.CreateToken(null, null, "two", "bobuser", 200);

        Assert.Equal(2, tokenService.DeleteTokensByAdmin(200));
        Assert.Null(tokenService.GetToken(first));
        Assert.Null(tokenService.GetToken(second));
    }

    [Fact]
    public void GetRoles_UnknownAdmin_ReturnsEmpty()
    {
        SetOwner();
        var service = CreateService();
        service.EnsureBootstrapped();

        Assert.Empty(service.GetRoles(999));
    }
}
