using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Middleware;

public sealed class RequirePermissionFilterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-perms-{Guid.NewGuid():N}.db");
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly TokenService _tokenService;
    private readonly AdminService _adminService;

    private const long OwnerId = 100;
    private const long AdminId = 200;

    public RequirePermissionFilterTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_ => new TokenDbContext(Options.Create(new LiteDbSettings { Path = _dbPath })));
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AdminService>();
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.ParsedAdmins = [new AdminUser(OwnerId, "alice")];
        });
        builder.Services.Configure<AuthSettings>(_ => { });

        var app = builder.Build();
        app.UseTokenAuth();

        app.MapGet("/api/open", () => Results.Ok("ok"));
        app.MapGet("/api/restricted", () => Results.Ok("ok"))
            .RequirePermission(AdminPermissions.ConfigRead);

        var services = app.Services;
        var adminService = services.GetRequiredService<AdminService>();
        adminService.EnsureBootstrapped();
        Assert.True(adminService.AddAcceptedAdmin(AdminId, "bobuser", "test"));
        _tokenService = services.GetRequiredService<TokenService>();
        _adminService = services.GetRequiredService<AdminService>();

        app.StartAsync().GetAwaiter().GetResult();
        _app = app;
        _client = app.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private HttpClient ClientWithSession()
    {
        var tokenId = _tokenService.CreateToken(null, null, "test", "bobuser", AdminId);
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"auth_token={tokenId}");
        return client;
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/restricted");

        Assert.Equal(StatusCodes.Status401Unauthorized, (int)response.StatusCode);
    }

    [Fact]
    public async Task WithToken_OpenEndpoint_Returns200()
    {
        using var client = ClientWithSession();

        var response = await client.GetAsync("/api/open");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ViewerRole_RestrictedEndpoint_Returns403()
    {
        Assert.True(_adminService.UpdateRoles(AdminId, Array.Empty<AdminRole>(), "test"));
        using var client = ClientWithSession();

        var response = await client.GetAsync("/api/restricted");

        Assert.Equal(StatusCodes.Status403Forbidden, (int)response.StatusCode);
    }

    [Fact]
    public async Task AllowedRole_RestrictedEndpoint_Returns200()
    {
        Assert.True(_adminService.UpdateRoles(AdminId, new[] { AdminRole.OperationsAdmin }, "test"));
        using var client = ClientWithSession();

        var response = await client.GetAsync("/api/restricted");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task OwnerRole_RestrictedEndpoint_Returns200()
    {
        var tokenId = _tokenService.CreateToken(null, null, "owner", "alice", OwnerId);
        using var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"auth_token={tokenId}");

        var response = await client.GetAsync("/api/restricted");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DisallowedRole_RestrictedEndpoint_Returns403()
    {
        Assert.True(_adminService.UpdateRoles(AdminId, new[] { AdminRole.Support }, "test"));
        using var client = ClientWithSession();

        var response = await client.GetAsync("/api/restricted");

        Assert.Equal(StatusCodes.Status403Forbidden, (int)response.StatusCode);
    }

    [Fact]
    public async Task RoleChange_TakesEffectImmediately()
    {
        Assert.True(_adminService.UpdateRoles(AdminId, new[] { AdminRole.OperationsAdmin }, "test"));
        using var client = ClientWithSession();

        Assert.True((await client.GetAsync("/api/restricted")).IsSuccessStatusCode);

        Assert.True(_adminService.UpdateRoles(AdminId, new[] { AdminRole.Support }, "test"));

        Assert.Equal(StatusCodes.Status403Forbidden, (int)(await client.GetAsync("/api/restricted")).StatusCode);
    }

    [Fact]
    public async Task LegacyToken_WithoutAdminAssociation_IsViewer()
    {
        var tokenId = _tokenService.CreateToken(null, null, "legacy");
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"auth_token={tokenId}");

        var response = await client.GetAsync("/api/restricted");

        Assert.Equal(StatusCodes.Status403Forbidden, (int)response.StatusCode);
    }
}
