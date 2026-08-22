using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Middleware;

public sealed class RequireStepUpFilterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-stepup-{Guid.NewGuid():N}.db");
    private readonly string _auditDbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-stepup-audit-{Guid.NewGuid():N}.db");
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly TokenService _tokenService;
    private readonly StepUpService _stepUpService;
    private readonly AuthToken _token;

    private const long AdminId = 100;

    public RequireStepUpFilterTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_ => new TokenDbContext(Options.Create(new LiteDbSettings { Path = _dbPath })));
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AdminService>();
        builder.Services.AddSingleton<StepUpService>();
        builder.Services.AddSingleton(_ => new AuditDbContext(Options.Create(new AuditDbSettings { Path = _auditDbPath })));
        builder.Services.AddSingleton<AuditService>();
        builder.Services.AddSingleton<IStepUpSender>(new NoopStepUpSender());
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.ParsedAdmins = [new AdminUser(AdminId, "alice"), new AdminUser(200, "bob")];
        });
        builder.Services.Configure<AuthSettings>(_ => { });

        var app = builder.Build();
        app.UseTokenAuth();

        app.MapPost("/api/critical/{name}", (string name) => Results.Ok(new { name }))
            .RequireStepUp("docker.branch", context => $"container={context.Request.RouteValues["name"]}");

        app.MapPost("/api/stepup/request", async (StepUpRequestDto dto, HttpContext context, StepUpService stepUpService) =>
        {
            var token = (context.Items["AuthToken"] as AuthToken)!;
            var request = stepUpService.CreateRequest(new PendingStepUp
            {
                ActionKey = dto.Action,
                Params = dto.Parameters ?? string.Empty,
                TokenId = token.Id,
                TargetTelegramUserId = token.ApprovedByTelegramUserId!.Value
            });
            await Task.CompletedTask;
            return Results.Ok(new { confirmationId = request.ConfirmationId });
        });

        var services = app.Services;
        services.GetRequiredService<AdminService>().EnsureBootstrapped();
        _tokenService = services.GetRequiredService<TokenService>();
        _stepUpService = services.GetRequiredService<StepUpService>();

        app.StartAsync().GetAwaiter().GetResult();
        _app = app;
        _client = app.GetTestClient();

        var tokenId = _tokenService.CreateToken(null, null, "test", "alice", AdminId);
        _client.DefaultRequestHeaders.Add("Cookie", $"auth_token={tokenId}");
        _token = _tokenService.GetToken(tokenId)!;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { File.Delete(_dbPath); } catch (IOException) { }
        try { File.Delete(_auditDbPath); } catch (IOException) { }
    }

    private string Approve(string actionKey, string parameters)
    {
        var request = new PendingStepUp
        {
            ActionKey = actionKey,
            Params = parameters,
            TokenId = _token.Id,
            TargetTelegramUserId = AdminId
        };
        _stepUpService.CreateRequest(request);
        Assert.True(_stepUpService.Resolve(request.ConfirmationId, StepUpStatus.Approved, AdminId));
        return request.ConfirmationId;
    }

    [Fact]
    public async Task WithoutConfirmation_Returns428WithActionInfo()
    {
        var response = await _client.PostAsJsonAsync("/api/critical/users", new { });

        Assert.Equal(StatusCodes.Status428PreconditionRequired, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("step_up_required", body.GetProperty("error").GetString());
        Assert.Equal("docker.branch", body.GetProperty("action").GetString());
        Assert.Equal("container=users", body.GetProperty("parameters").GetString());
    }

    [Fact]
    public async Task WithValidConfirmation_Succeeds()
    {
        var confirmationId = Approve("docker.branch", "container=users");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/critical/users")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Confirmation-Id", confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Confirmation_IsSingleUse()
    {
        var confirmationId = Approve("docker.branch", "container=users");

        var first = new HttpRequestMessage(HttpMethod.Post, "/api/critical/users") { Content = JsonContent.Create(new { }) };
        first.Headers.Add("X-Confirmation-Id", confirmationId);
        var second = new HttpRequestMessage(HttpMethod.Post, "/api/critical/users") { Content = JsonContent.Create(new { }) };
        second.Headers.Add("X-Confirmation-Id", confirmationId);

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(first)).StatusCode);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, (int)(await _client.SendAsync(second)).StatusCode);
    }

    [Fact]
    public async Task Confirmation_ForOtherContainer_DoesNotApply()
    {
        var confirmationId = Approve("docker.branch", "container=users");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/critical/messages")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Confirmation-Id", confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(StatusCodes.Status428PreconditionRequired, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("container=messages", body.GetProperty("parameters").GetString());
    }

    [Fact]
    public async Task Confirmation_ViaQueryParameter_Works()
    {
        var confirmationId = Approve("docker.branch", "container=users");

        var response = await _client.PostAsJsonAsync($"/api/critical/users?confirmation={confirmationId}", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class NoopStepUpSender : IStepUpSender
    {
        public Task SendStepUpRequestAsync(PendingStepUp request) => Task.CompletedTask;
    }
}
