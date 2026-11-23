using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using Grpc.Core;

using MassTransit;

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
public sealed class NotificationsEndpointsTests : IDisposable
{
    private const long OwnerId = 100;
    private const long ModeratorId = 300;
    private const long SupportId = 200;
    private const long UserId = 42;
    private const long UserWithoutDevicesId = 43;
    private const long UnknownUserId = 999;

    private static readonly Guid FirstDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondDeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adminpanel-notifications-endpoints-{Guid.NewGuid():N}");
    private readonly TokenDbContext _tokenDb;
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly AdminService _adminService;
    private readonly Guid _ownerToken;
    private readonly Guid _moderatorToken;
    private readonly Guid _supportToken;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();
    private readonly Mock<IPublishEndpoint> _publishEndpoint = new();
    private readonly Dictionary<long, List<Device>> _devicesByUser = new()
    {
        [UserId] =
        [
            new Device
            {
                DeviceId = FirstDeviceId.ToString(),
                UserId = UserId,
                CustomName = "Pixel 8",
                AppName = "BarkFluff Android",
                OperationSystem = "Android 15",
                NotificationsEnabled = true
            },
            new Device
            {
                DeviceId = SecondDeviceId.ToString(),
                UserId = UserId,
                OriginalName = "Chrome",
                AppName = "BarkFluff Web",
                OperationSystem = "Windows 11",
                NotificationsEnabled = false
            }
        ]
    };

    public NotificationsEndpointsTests()
    {
        Directory.CreateDirectory(_directory);
        _tokenDb = new TokenDbContext(Options.Create(new LiteDbSettings
        {
            Path = Path.Combine(_directory, "tokens.db")
        }));

        ConfigureClients();
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<AdminBroadcastNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_tokenDb);
        builder.Services.AddSingleton(_usersClient.Object);
        builder.Services.AddSingleton(_publishEndpoint.Object);
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<AdminService>();
        builder.Services.AddScoped<NotificationTestService>();
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.ParsedAdmins = [new AdminUser(OwnerId, "alice")];
        });
        builder.Services.Configure<AuthSettings>(_ => { });

        _app = builder.Build();
        _app.UseTokenAuth();
        _app.MapNotificationsEndpoints();

        _adminService = _app.Services.GetRequiredService<AdminService>();
        _adminService.EnsureBootstrapped();
        _adminService.AddAcceptedAdmin(ModeratorId, "moderator", "alice");
        _adminService.UpdateRoles(ModeratorId, new[] { AdminRole.ContentAdmin }, "alice");
        _adminService.AddAcceptedAdmin(SupportId, "supportuser", "alice");
        _adminService.UpdateRoles(SupportId, new[] { AdminRole.Support }, "alice");

        var tokenService = _app.Services.GetRequiredService<TokenService>();
        _ownerToken = tokenService.CreateToken(null, null, "owner", "alice", OwnerId);
        _moderatorToken = tokenService.CreateToken(null, null, "moderator", "moderator", ModeratorId);
        _supportToken = tokenService.CreateToken(null, null, "support", "supportuser", SupportId);

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task GetRecipient_WithoutLink_ReturnsNull()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/notifications/test-recipient");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("recipient").ValueKind);
    }

    [Fact]
    public async Task SetRecipient_StoresLinkAndReturnsUserWithDevices()
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recipient = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipient");
        Assert.Equal(UserId, recipient.GetProperty("userId").GetInt64());
        Assert.Equal("alice_bf", recipient.GetProperty("username").GetString());
        var devices = recipient.GetProperty("devices").EnumerateArray().ToList();
        Assert.Equal(2, devices.Count);
        Assert.Equal("Pixel 8", devices[0].GetProperty("name").GetString());
        Assert.False(devices[1].GetProperty("notificationsEnabled").GetBoolean());

        // Привязка читается заново из хранилища, а не из ответа.
        Assert.Equal(UserId, _adminService.GetRecord(OwnerId)!.BarkFluffUserId);
        using var reread = await SendAsync(HttpMethod.Get, "/api/notifications/test-recipient");
        var rereadRecipient = (await reread.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipient");
        Assert.Equal(UserId, rereadRecipient.GetProperty("userId").GetInt64());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SetRecipient_RejectsNonPositiveId(long barkFluffUserId)
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_adminService.GetRecord(OwnerId)!.BarkFluffUserId);
    }

    [Fact]
    public async Task SetRecipient_UnknownUser_Returns404AndKeepsPreviousLink()
    {
        await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserId });

        using var response = await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UnknownUserId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserId, _adminService.GetRecord(OwnerId)!.BarkFluffUserId);
    }

    [Fact]
    public async Task SetRecipient_UsersServiceUnavailable_Returns503WithoutLinking()
    {
        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "users down")));

        using var response = await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserId });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("временно недоступен", json.GetProperty("message").GetString());
        Assert.Null(_adminService.GetRecord(OwnerId)!.BarkFluffUserId);
    }

    [Fact]
    public async Task TestSend_PublishesOnlyLinkedUserDevices()
    {
        await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserId });

        using var response = await SendAsync(HttpMethod.Post, "/api/notifications/test", body: new { title = "  Проверка  ", body = "Текст" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("enqueued").GetBoolean());
        Assert.Equal(2, json.GetProperty("deviceCount").GetInt32());

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<AdminBroadcastNotificationEvent>(e =>
                e.Title == "Проверка" &&
                e.Body == "Текст" &&
                e.TargetDeviceIds.Count == 2 &&
                e.TargetDeviceIds.Contains(FirstDeviceId) &&
                e.TargetDeviceIds.Contains(SecondDeviceId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestSend_WithoutLink_Returns400AndDoesNotPublish()
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/notifications/test", body: new { title = "Проверка", body = "Текст" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("не привязан", json.GetProperty("message").GetString());
        VerifyNothingPublished();
    }

    [Fact]
    public async Task TestSend_UserWithoutDevices_Returns400AndDoesNotPublish()
    {
        // Пустой TargetDeviceIds в событии означал бы рассылку на все устройства, поэтому публикации быть не должно.
        await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserWithoutDevicesId });

        using var response = await SendAsync(HttpMethod.Post, "/api/notifications/test", body: new { title = "Проверка", body = "Текст" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("У пользователя нет устройств.", json.GetProperty("message").GetString());
        VerifyNothingPublished();
    }

    [Fact]
    public async Task ClearRecipient_RemovesLinkAndKeepsAdminRoles()
    {
        await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", _moderatorToken, new { barkFluffUserId = UserId });

        using var response = await SendAsync(HttpMethod.Delete, "/api/notifications/test-recipient", _moderatorToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(_adminService.GetRecord(ModeratorId)!.BarkFluffUserId);
        Assert.Contains(AdminRole.ContentAdmin, _adminService.GetRoles(ModeratorId));
    }

    [Fact]
    public async Task BootstrapRestart_KeepsRecipientLink()
    {
        await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", body: new { barkFluffUserId = UserId });

        _adminService.EnsureBootstrapped();

        Assert.Equal(UserId, _adminService.GetRecord(OwnerId)!.BarkFluffUserId);
    }

    [Fact]
    public async Task SupportRole_CannotManageTestNotifications()
    {
        using var setResponse = await SendAsync(HttpMethod.Put, "/api/notifications/test-recipient", _supportToken, new { barkFluffUserId = UserId });
        using var sendResponse = await SendAsync(HttpMethod.Post, "/api/notifications/test", _supportToken, new { title = "Проверка", body = "Текст" });

        Assert.Equal(HttpStatusCode.Forbidden, setResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sendResponse.StatusCode);
        Assert.Null(_adminService.GetRecord(SupportId)!.BarkFluffUserId);
        VerifyNothingPublished();
    }

    private void VerifyNothingPublished()
    {
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<AdminBroadcastNotificationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"auth_token={token ?? _ownerToken}");
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request);
    }

    private void ConfigureClients()
    {
        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns((GetByIdRequest request, Metadata _, DateTime? __, CancellationToken ___) =>
            {
                // Как в Users: для неизвестного пользователя User остаётся пустым, а не null.
                var response = new GetByIdResponse();
                if (request.UserId is UserId or UserWithoutDevicesId)
                {
                    response.User = new User
                    {
                        Id = request.UserId,
                        Username = "alice_bf",
                        FirstName = "Alice",
                        LastName = "Example"
                    };
                }

                return UnaryCall(response);
            });

        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns((GetUserDevicesRequest request, Metadata _, DateTime? __, CancellationToken ___) =>
            {
                var response = new GetUserDevicesResponse();
                if (_devicesByUser.TryGetValue(request.UserId, out var devices))
                    response.Devices.AddRange(devices);

                return UnaryCall(response);
            });
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
