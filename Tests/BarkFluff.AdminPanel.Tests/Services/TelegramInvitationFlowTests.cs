using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Net;
using System.Text;
using System.Text.Json;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public sealed class TelegramInvitationFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adminpanel-telegram-invitations-{Guid.NewGuid():N}");
    private readonly TokenDbContext _db;
    private readonly AuditDbContext _auditDb;
    private readonly AuditService _auditService;
    private readonly AdminService _adminService;
    private readonly AdminInvitationService _invitationService;
    private readonly StepUpService _stepUpService;
    private readonly TelegramApiHandler _apiHandler = new();
    private readonly HttpClient _httpClient;
    private readonly ITelegramBotClient _botClient;
    private readonly UpdateHandler _handler;

    public TelegramInvitationFlowTests()
    {
        Directory.CreateDirectory(_directory);
        _db = new TokenDbContext(Options.Create(new LiteDbSettings
        {
            Path = Path.Combine(_directory, "tokens.db")
        }));
        _auditDb = new AuditDbContext(Options.Create(new AuditDbSettings
        {
            Path = Path.Combine(_directory, "audit.db")
        }));
        _auditService = new AuditService(_auditDb, NullLogger<AuditService>.Instance);
        var telegramSettings = new TelegramSettings
        {
            ParsedAdmins = [new AdminUser(100, "alice")]
        };
        _adminService = new AdminService(_db, Options.Create(telegramSettings), NullLogger<AdminService>.Instance);
        _adminService.EnsureBootstrapped();
        _invitationService = new AdminInvitationService(
            _db,
            _adminService,
            NullLogger<AdminInvitationService>.Instance);
        _stepUpService = new StepUpService();
        _httpClient = new HttpClient(_apiHandler);
        _botClient = new TelegramBotClient("123:TEST", _httpClient);
        _handler = new UpdateHandler(
            _botClient,
            new PendingAuthService(Options.Create(new AuthSettings())),
            new TokenService(_db, Options.Create(new AuthSettings())),
            _adminService,
            _invitationService,
            _stepUpService,
            _auditService,
            Options.Create(new AuthSettings()),
            NullLogger.Instance);
    }

    public void Dispose()
    {
        _stepUpService.Dispose();
        _httpClient.Dispose();
        _auditDb.Dispose();
        _db.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Start_WithMatchingIdentity_SendsInvitationButtons()
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        await _handler.HandleUpdateAsync(_botClient, CreateMessageUpdate(200, "BobUser", $"/start {invitation.Payload}"), CancellationToken.None);

        var request = Assert.Single(_apiHandler.Requests);
        Assert.Equal("sendMessage", request.Method);
        Assert.Contains($"admininvite:{invitation.Payload}:accept", request.Body);
        Assert.Contains($"admininvite:{invitation.Payload}:reject", request.Body);
        Assert.Equal(AdminInvitationStatus.Pending, _invitationService.Get(invitation.Id)!.Status);
        Assert.Null(_adminService.GetRecord(200));
    }

    [Theory]
    [InlineData(201, "bobuser")]
    [InlineData(200, "different_user")]
    public async Task Start_WithMismatchingIdentityDoesNotOfferAcceptance(long userId, string username)
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        await _handler.HandleUpdateAsync(_botClient, CreateMessageUpdate(userId, username, $"/start {invitation.Payload}"), CancellationToken.None);

        var request = Assert.Single(_apiHandler.Requests);
        Assert.Equal("sendMessage", request.Method);
        using var body = JsonDocument.Parse(request.Body);
        var sentText = body.RootElement.GetProperty("text").GetString();
        Assert.Contains("другого Telegram-пользователя", sentText);
        Assert.DoesNotContain($"admininvite:{invitation.Payload}:accept", request.Body);
        Assert.Equal(AdminInvitationStatus.Pending, _invitationService.Get(invitation.Id)!.Status);
        Assert.Null(_adminService.GetRecord(200));
    }

    [Theory]
    [InlineData("accept", AdminInvitationStatus.Accepted)]
    [InlineData("reject", AdminInvitationStatus.Rejected)]
    public async Task Callback_TargetUserResolvesInvitation(string action, AdminInvitationStatus expectedStatus)
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        await _handler.HandleUpdateAsync(
            _botClient,
            CreateCallbackUpdate(200, "BobUser", $"admininvite:{invitation.Payload}:{action}"),
            CancellationToken.None);

        Assert.Equal(expectedStatus, _invitationService.Get(invitation.Id)!.Status);
        if (expectedStatus == AdminInvitationStatus.Accepted)
            Assert.NotNull(_adminService.GetRecord(200));
        else
            Assert.Null(_adminService.GetRecord(200));
        Assert.Contains(_apiHandler.Requests, request => request.Method == "editMessageText");
        Assert.Contains(_apiHandler.Requests, request => request.Method == "answerCallbackQuery");
    }

    [Theory]
    [InlineData(201, "bobuser")]
    [InlineData(200, "different_user")]
    public async Task Callback_FromWrongTelegramIdentityCannotResolveInvitation(long userId, string username)
    {
        var invitation = _invitationService.Create(200, "bobuser", "alice", "barkbot").Invitation!;

        await _handler.HandleUpdateAsync(
            _botClient,
            CreateCallbackUpdate(userId, username, $"admininvite:{invitation.Payload}:accept"),
            CancellationToken.None);

        Assert.Equal(AdminInvitationStatus.Pending, _invitationService.Get(invitation.Id)!.Status);
        Assert.Null(_adminService.GetRecord(200));
        Assert.DoesNotContain(_apiHandler.Requests, request => request.Method == "editMessageText");
    }

    private static Update CreateMessageUpdate(long userId, string username, string text)
    {
        return new Update
        {
            Id = 1,
            Message = new Message
            {
                Id = 10,
                Chat = new Chat { Id = userId, Type = ChatType.Private },
                From = new User { Id = userId, Username = username, FirstName = "Test" },
                Text = text
            }
        };
    }

    private static Update CreateCallbackUpdate(long userId, string username, string data)
    {
        return new Update
        {
            Id = 2,
            CallbackQuery = new CallbackQuery
            {
                Id = "callback-1",
                From = new User { Id = userId, Username = username, FirstName = "Test" },
                Data = data,
                Message = new Message
                {
                    Id = 10,
                    Chat = new Chat { Id = userId, Type = ChatType.Private },
                    From = new User { Id = 999, IsBot = true, Username = "barkbot", FirstName = "Bark" },
                    Text = "invite"
                }
            }
        };
    }

    private sealed class TelegramApiHandler : HttpMessageHandler
    {
        public List<ApiRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var method = request.RequestUri?.AbsolutePath.Split('/').Last() ?? string.Empty;
            Requests.Add(new ApiRequest(method, body));

            var responseBody = method == "answerCallbackQuery"
                ? "{\"ok\":true,\"result\":true}"
                : "{\"ok\":true,\"result\":{\"message_id\":42,\"chat\":{\"id\":200,\"type\":\"private\"},\"date\":1700000000,\"text\":\"ok\"}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record ApiRequest(string Method, string Body);
}
