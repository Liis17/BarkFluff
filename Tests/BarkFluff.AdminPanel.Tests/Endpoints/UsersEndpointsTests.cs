using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Endpoints;

[Collection("LiteDb")]
public sealed class UsersEndpointsTests : IDisposable
{
    private const long OwnerId = 100;
    private const long SupportId = 200;
    private const long UserId = 42;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adminpanel-users-endpoints-{Guid.NewGuid():N}");
    private readonly TokenDbContext _tokenDb;
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly TokenService _tokenService;
    private readonly AdminService _adminService;
    private readonly StepUpService _stepUpService;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient = new();
    private readonly Mock<IdentityServerApi.IdentityServerApiClient> _identityClient = new();
    private readonly Guid _ownerToken;
    private SetAccountRestrictionServerRequest? _lastRestrictionRequest;
    private SearchUsersServerRequest? _lastSearchRequest;

    public UsersEndpointsTests()
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

        ConfigureClients();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_tokenDb);
        builder.Services.AddSingleton(auditDb);
        builder.Services.AddSingleton(_usersClient.Object);
        builder.Services.AddSingleton(_filesClient.Object);
        builder.Services.AddSingleton(_identityClient.Object);
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AdminService>();
        builder.Services.AddScoped<UserSessionRevocationService>();
        builder.Services.AddSingleton<StepUpService>();
        builder.Services.AddSingleton<AuditService>();
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.ParsedAdmins = [new AdminUser(OwnerId, "alice")];
        });
        builder.Services.Configure<AuthSettings>(_ => { });

        _app = builder.Build();
        _app.UseTokenAuth();
        _app.MapUsersEndpoints();

        _adminService = _app.Services.GetRequiredService<AdminService>();
        _adminService.EnsureBootstrapped();
        _tokenService = _app.Services.GetRequiredService<TokenService>();
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
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("POST", "block", StepUpActions.UsersBlock, "BLOCK", AccountRestrictionAction.Block)]
    [InlineData("POST", "unblock", StepUpActions.UsersUnblock, "UNBLOCK", AccountRestrictionAction.Unblock)]
    [InlineData("POST", "deletion", StepUpActions.UsersDeletionSchedule, "SCHEDULE_DELETION", AccountRestrictionAction.ScheduleDeletion)]
    [InlineData("DELETE", "deletion", StepUpActions.UsersDeletionCancel, "CANCEL_DELETION", AccountRestrictionAction.CancelDeletion)]
    public async Task RestrictionRoutes_RequireBoundStepUpAndCallIdentity(
        string method,
        string path,
        string actionKey,
        string actionName,
        AccountRestrictionAction expectedAction)
    {
        const string reason = "Repeated abuse";
        var parameters = $"userId={UserId};action={actionName};reason={reason}";
        var confirmationId = ApproveStepUp(actionKey, parameters);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/users/{UserId}/{path}")
        {
            Content = JsonContent.Create(new { reason })
        };
        request.Headers.Add(RequireStepUpFilter.HeaderName, confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserId, _lastRestrictionRequest?.UserId);
        Assert.Equal(expectedAction, _lastRestrictionRequest?.Action);
        Assert.Equal(reason, _lastRestrictionRequest?.Reason);
    }

    [Theory]
    [InlineData("POST", "block")]
    [InlineData("POST", "unblock")]
    [InlineData("POST", "deletion")]
    [InlineData("DELETE", "deletion")]
    public async Task RestrictionRoute_RejectsMissingReasonBeforeCallingIdentity(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/users/{UserId}/{path}")
        {
            Content = JsonContent.Create(new { reason = "   " })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _identityClient.Verify(client => client.SetAccountRestrictionServerAsync(
            It.IsAny<SetAccountRestrictionServerRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestrictionRoute_RejectsStepUpApprovedForDifferentParameters()
    {
        const string reason = "Repeated abuse";
        var confirmationId = ApproveStepUp(StepUpActions.UsersBlock, $"userId=43;action=BLOCK;reason={reason}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{UserId}/block")
        {
            Content = JsonContent.Create(new { reason })
        };
        request.Headers.Add(RequireStepUpFilter.HeaderName, confirmationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"userId={UserId};action=BLOCK;reason={reason}", body.RootElement.GetProperty("parameters").GetString());
        _identityClient.Verify(client => client.SetAccountRestrictionServerAsync(
            It.IsAny<SetAccountRestrictionServerRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestrictionRoute_RequiresSecurityAdminPermission()
    {
        Assert.True(_adminService.AddAcceptedAdmin(SupportId, "supportuser", "test"));
        Assert.True(_adminService.UpdateRoles(SupportId, [AdminRole.Support], "test"));
        var supportToken = _tokenService.CreateToken(null, null, "support", "supportuser", SupportId);
        using var client = _app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{UserId}/block")
        {
            Content = JsonContent.Create(new { reason = "Repeated abuse" })
        };
        request.Headers.Add("Cookie", $"auth_token={supportToken}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _identityClient.Verify(client => client.SetAccountRestrictionServerAsync(
            It.IsAny<SetAccountRestrictionServerRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("blocked", AccountStatus.Blocked)]
    [InlineData("pending_deletion", AccountStatus.PendingDeletion)]
    public async Task Search_FiltersInUsersAndReturnsRestrictionDetails(string status, AccountStatus expectedStatus)
    {
        var response = await _client.GetAsync($"/api/users?query=alice&status={status}&offset=5&size=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_lastSearchRequest);
        Assert.True(_lastSearchRequest!.HasStatusFilter);
        Assert.Equal(expectedStatus, _lastSearchRequest.StatusFilter);
        Assert.Equal(5, _lastSearchRequest.Offset);
        Assert.Equal(20, _lastSearchRequest.Size);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(37, body.RootElement.GetProperty("totalCount").GetInt32());
        var user = Assert.Single(body.RootElement.GetProperty("users").EnumerateArray());
        Assert.Equal(expectedStatus.ToString(), user.GetProperty("accountStatus").GetString());
        Assert.Equal("Server policy", user.GetProperty("restrictionReason").GetString());
        Assert.True(user.GetProperty("restrictedAt").ValueKind == JsonValueKind.String);
    }

    private void ConfigureClients()
    {
        _usersClient
            .Setup(client => client.GetByIdAsync(It.IsAny<GetByIdRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns((GetByIdRequest request, Metadata _, DateTime? __, CancellationToken ___) => UnaryCall(new GetByIdResponse
            {
                User = new User { Id = request.UserId }
            }));

        _usersClient
            .Setup(client => client.SearchUsersServerAsync(It.IsAny<SearchUsersServerRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback((SearchUsersServerRequest request, Metadata _, DateTime? __, CancellationToken ___) => _lastSearchRequest = request)
            .Returns((SearchUsersServerRequest request, Metadata _, DateTime? __, CancellationToken ___) =>
                UnaryCall(CreateSearchResponse(request.HasStatusFilter ? request.StatusFilter : AccountStatus.Blocked)));

        _identityClient
            .Setup(client => client.SetAccountRestrictionServerAsync(It.IsAny<SetAccountRestrictionServerRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback((SetAccountRestrictionServerRequest request, Metadata _, DateTime? __, CancellationToken ___) => _lastRestrictionRequest = request)
            .Returns((SetAccountRestrictionServerRequest _, Metadata __, DateTime? ___, CancellationToken ____) => UnaryCall(new SetAccountRestrictionServerResponse
            {
                Restriction = CreateRestriction()
            }));
    }

    private static SearchUsersServerResponse CreateSearchResponse(AccountStatus status)
    {
        var user = new User
        {
            Id = UserId,
            FirstName = "Alice",
            Username = "alice",
            AccountStatus = status
        };
        return new SearchUsersServerResponse
        {
            TotalCount = 37,
            Users = { user },
            Results =
            {
                new SearchUserServerResult { User = user, Restriction = CreateRestriction(status) }
            }
        };
    }

    private static AccountRestrictionInfo CreateRestriction(AccountStatus status = AccountStatus.Blocked) => new()
    {
        Status = status,
        Reason = "Server policy",
        RestrictedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
        DeletionScheduledAt = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), DateTimeKind.Utc))
    };

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

    private static AsyncUnaryCall<T> UnaryCall<T>(T response)
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            () => { });
    }
}
