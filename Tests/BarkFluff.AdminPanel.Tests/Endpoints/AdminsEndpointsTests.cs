using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Net;
using System.Text;
using System.Text.Json;

using Telegram.Bot;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Endpoints;

public sealed class AdminsEndpointsTests : IDisposable
{
    private const long OwnerId = 100;
    private const long DynamicAdminId = 200;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adminpanel-admin-endpoints-{Guid.NewGuid():N}");
    private readonly TokenDbContext _tokenDb;
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly TokenService _tokenService;
    private readonly AdminService _adminService;
    private readonly AdminInvitationService _invitationService;
    private readonly StepUpService _stepUpService;
    private readonly Guid _ownerToken;
    private readonly HttpClient _telegramHttpClient;

    public AdminsEndpointsTests()
    {
        Directory.CreateDirectory(_directory);
        _tokenDb = new TokenDbContext(Options.Create(new LiteDbSettings
        {
            Path = Path.Combine(_directory, "tokens.db")
        }));
        var auditDb = new AuditDbContext(Options.Create(new AuditDbSettings
        {
            Path = Path.Combine(_directory, "audit.db")
        }));
        _telegramHttpClient = new HttpClient(new TelegramApiHandler());
        var telegramBot = new TelegramBotClient("123:TEST", _telegramHttpClient);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_tokenDb);
        builder.Services.AddSingleton(auditDb);
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AdminService>();
        builder.Services.AddSingleton<AdminInvitationService>();
        builder.Services.AddSingleton<StepUpService>();
        builder.Services.AddSingleton<AuditService>();
        builder.Services.AddSingleton<PendingAuthService>();
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.BotToken = "123:TEST";
            settings.ParsedAdmins = [new AdminUser(OwnerId, "alice")];
        });
        builder.Services.Configure<AuthSettings>(_ => { });
        builder.Services.AddSingleton<TelegramBotService>(services => new TelegramBotService(
            services.GetRequiredService<IOptions<TelegramSettings>>(),
            services.GetRequiredService<PendingAuthService>(),
            services.GetRequiredService<TokenService>(),
            services.GetRequiredService<AdminService>(),
            services.GetRequiredService<AdminInvitationService>(),
            services.GetRequiredService<StepUpService>(),
            services.GetRequiredService<AuditService>(),
            services.GetRequiredService<IOptions<AuthSettings>>(),
            services.GetRequiredService<ILogger<TelegramBotService>>(),
            telegramBot));

        _app = builder.Build();
        _app.UseTokenAuth();
        _app.MapAdminsEndpoints();

        _adminService = _app.Services.GetRequiredService<AdminService>();
        _adminService.EnsureBootstrapped();
        Assert.True(_adminService.AddAcceptedAdmin(DynamicAdminId, "bobuser", "test"));

        _tokenService = _app.Services.GetRequiredService<TokenService>();
        _invitationService = _app.Services.GetRequiredService<AdminInvitationService>();
        _stepUpService = _app.Services.GetRequiredService<StepUpService>();
        _ownerToken = _tokenService.CreateToken(null, null, "owner", "alice", OwnerId);

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("Cookie", $"auth_token={_ownerToken}");
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _telegramHttpClient.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task List_ReturnsOwnerMarkerAndAvatarUrl()
    {
        var response = await _client.GetAsync("/api/admins");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var owner = document.RootElement.EnumerateArray().Single(item => item.GetProperty("telegramUserId").GetInt64() == OwnerId);
        var dynamicAdmin = document.RootElement.EnumerateArray().Single(item => item.GetProperty("telegramUserId").GetInt64() == DynamicAdminId);

        Assert.True(owner.GetProperty("isOwner").GetBoolean());
        Assert.Equal(new[] { "Owner" }, owner.GetProperty("roles").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.False(dynamicAdmin.GetProperty("isOwner").GetBoolean());
        Assert.Contains($"/api/admins/{OwnerId}/avatar", owner.GetProperty("avatarUrl").GetString());
        Assert.Contains($"/api/admins/{DynamicAdminId}/avatar", dynamicAdmin.GetProperty("avatarUrl").GetString());
    }

    [Fact]
    public async Task UpdateRoles_CannotChangeOwner()
    {
        const string parameters = "target=100;roles=Support";
        var confirmationId = ApproveStepUp(StepUpActions.AdminsRolesUpdate, parameters);
        using var content = new StringContent("{\"roles\":[\"Support\"]}", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admins/100/roles")
        {
            Content = content
        };
        request.Headers.Add(RequireStepUpFilter.HeaderName, confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { AdminRole.Owner }, _adminService.GetRoles(OwnerId));
    }

    [Fact]
    public async Task Delete_RemovesAdminRevokesTokensAndExpiresInvitations()
    {
        var tokenId = _tokenService.CreateToken(null, null, "dynamic", "bobuser", DynamicAdminId);
        var invitation = new AdminInvitation
        {
            TelegramUserId = DynamicAdminId,
            Username = "newuser",
            CreatedBy = "alice",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            Status = AdminInvitationStatus.Pending
        };
        _tokenDb.AdminInvitations.Insert(invitation);
        const string parameters = "target=200";
        var confirmationId = ApproveStepUp(StepUpActions.AdminsDelete, parameters);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/admins/200");
        request.Headers.Add(RequireStepUpFilter.HeaderName, confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(_adminService.GetRecord(DynamicAdminId));
        Assert.Null(_tokenService.GetToken(tokenId));
        Assert.Equal(AdminInvitationStatus.Expired, _invitationService.Get(invitation.Id)!.Status);
    }

    [Fact]
    public async Task InvitationEndpoints_CreateAndReturnStatus()
    {
        const string parameters = "target=300;username=newuser";
        var confirmationId = ApproveStepUp(StepUpActions.AdminsInvite, parameters);
        using var content = new StringContent(
            "{\"telegramUserId\":300,\"username\":\"@newuser\"}",
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admins/invitations")
        {
            Content = content
        };
        request.Headers.Add(RequireStepUpFilter.HeaderName, confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invitationId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("pending", created.RootElement.GetProperty("status").GetString());
        Assert.StartsWith("https://t.me/barkbot?start=", created.RootElement.GetProperty("link").GetString());

        var statusResponse = await _client.GetAsync($"/api/admins/invitations/{invitationId}");

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(invitationId, status.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("pending", status.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Avatar_ReturnsTelegramPhotoForActiveAdmin()
    {
        var response = await _client.GetAsync($"/api/admins/{OwnerId}/avatar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
    }

    private string ApproveStepUp(string actionKey, string parameters)
    {
        var request = _stepUpService.CreateRequest(new PendingStepUp
        {
            ActionKey = actionKey,
            Params = parameters,
            TokenId = _ownerToken,
            TargetTelegramUserId = OwnerId
        });
        Assert.True(_stepUpService.Resolve(request.ConfirmationId, StepUpStatus.Approved, OwnerId));
        return request.ConfirmationId;
    }

    private sealed class TelegramApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/file/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                });
            }

            var method = path.Split('/').Last();
            var body = method switch
            {
                "getMe" => "{\"ok\":true,\"result\":{\"id\":999,\"is_bot\":true,\"first_name\":\"BarkFluff\",\"username\":\"barkbot\"}}",
                "getUserProfilePhotos" => "{\"ok\":true,\"result\":{\"total_count\":1,\"photos\":[[{\"file_id\":\"avatar-file\",\"file_unique_id\":\"avatar-unique\",\"width\":64,\"height\":64,\"file_size\":3}]]}}",
                "getFile" => "{\"ok\":true,\"result\":{\"file_id\":\"avatar-file\",\"file_unique_id\":\"avatar-unique\",\"file_path\":\"photos/avatar.jpg\"}}",
                _ => "{\"ok\":true,\"result\":true}"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
