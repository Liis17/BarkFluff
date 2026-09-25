using System.Text.Json;
using System.Text.RegularExpressions;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Notifications;
using Grpc.Core;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class AuthenticationFlowTests
{
    [Fact]
    public async Task TelegramRegistration_WithoutSmtp_RequiresButtonAndReturnsOneSession()
    {
        using var h = new Harness();
        var pending = await h.Service.BeginRegistration(h.Registration(), default);
        Assert.StartsWith("https://t.me/test_bot?start=", pending.TelegramUrl);
        await h.StartBot(pending);
        Assert.Equal(AuthChallengeState.Waiting, (await h.Service.Status(pending.Challenge, default)).State);
        await h.Approve(999); // A different Telegram account cannot approve.
        Assert.Equal(AuthChallengeState.Waiting, (await h.Service.Status(pending.Challenge, default)).State);
        await h.Approve();
        var result = await h.Complete(pending);
        Assert.NotEmpty(result.Session.RefreshToken.Value);
        Assert.Equal(10, result.RecoveryCodes.Count);
        Assert.Empty(h.Emails);
        var repeated = await h.Complete(pending);
        Assert.Equal(result.Session.RefreshToken.Value, repeated.Session.RefreshToken.Value);
        Assert.Empty(repeated.RecoveryCodes);
        Assert.Equal(1, h.RegisteredDevices);
        Assert.Equal("", h.LastDraft!.Email);
    }

    [Fact]
    public async Task WrongPassword_DoesNotSendTelegramCode()
    {
        using var h = new Harness();
        await h.Register();
        var before = h.Bot.Messages.Count;
        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => h.Service.BeginSignIn(h.SignIn(password: "incorrect"), default));
        Assert.Equal(before, h.Bot.Messages.Count);
    }

    [Fact]
    public async Task PasswordAndTelegramCode_RejectsWrongCodesAndAcceptsDeliveredCode()
    {
        using var h = new Harness(); await h.Register();
        var pending = await h.Service.BeginSignIn(h.SignIn(), default);
        var wrong = await h.Complete(pending, "invalid");
        Assert.Null(wrong.Session);
        Assert.Equal("invalid_code", wrong.ErrorCode);
        var result = await h.Complete(pending, h.Bot.LastCode);
        Assert.NotNull(result.Session);
    }

    [Fact]
    public async Task FiveWrongCodes_InvalidateChallenge()
    {
        using var h = new Harness(); await h.Register();
        var pending = await h.Service.BeginSignIn(h.SignIn(), default);
        var valid = h.Bot.LastCode;
        for (var i = 0; i < 5; i++) await h.Complete(pending, "incorrect");
        var result = await h.Complete(pending, valid);
        Assert.Equal(AuthChallengeState.Rejected, result.State);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task RecoveryCode_IsSingleUse_AndDoesNotReplaceRequiredPassword()
    {
        using var h = new Harness(); var registration = await h.Register();
        var request = h.SignIn(); request.UseRecoveryCode = true;
        var first = await h.Service.BeginSignIn(request, default);
        Assert.NotNull((await h.Complete(first, registration.RecoveryCodes[0], true)).Session);
        var second = await h.Service.BeginSignIn(request, default);
        Assert.Null((await h.Complete(second, registration.RecoveryCodes[0], true)).Session);
        request.Password = "";
        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => h.Service.BeginSignIn(request, default));
    }

    [Fact]
    public async Task PasswordlessTelegram_RequiresApprovalFromBoundAccount()
    {
        using var h = new Harness(); await h.Register(AuthLoginMode.TelegramLogin);
        var pending = await h.Service.BeginSignIn(new BeginSignInRequest { Login = "newuser", LoginMode = AuthLoginMode.TelegramLogin }, default);
        await h.Approve(999);
        Assert.Equal(AuthChallengeState.Waiting, (await h.Service.Status(pending.Challenge, default)).State);
        await h.Approve();
        Assert.NotNull((await h.Complete(pending)).Session);
        await Assert.ThrowsAsync<RpcException>(() => h.Service.BeginSignIn(h.SignIn(), default));
    }

    [Fact]
    public async Task DisablingTelegram_CancelsPendingLogin_AndRetainsPasswordMode()
    {
        using var h = new Harness(); await h.Register();
        var waiting = await h.Service.BeginSignIn(h.SignIn(), default);
        var reauth = await h.Service.BeginReauthentication(new BeginReauthenticationRequest { Password = "password123", Factor = OtpTypeId.Telegram }, default);
        var proof = (await h.Complete(reauth, h.Bot.LastCode)).SecurityProof;
        var settings = await h.Service.UpdateSecuritySettings(new UpdateSecuritySettingsRequest
        { SecurityProof = proof, LoginMode = AuthLoginMode.Password }, false, default);
        Assert.False(settings.TelegramEnabled);
        Assert.True(settings.TelegramLinked);
        Assert.Equal(AuthChallengeState.Cancelled, (await h.Service.Status(waiting.Challenge, default)).State);
        var signIn = h.SignIn(); signIn.LoginMode = AuthLoginMode.Password;
        Assert.NotNull((await h.Complete(await h.Service.BeginSignIn(signIn, default))).Session);
    }

    [Fact]
    public async Task EmailRecovery_OnlyChangesPassword_StillRequiresTelegram()
    {
        using var h = new Harness(emailEnabled: true); await h.Register();
        var pending = await h.Service.BeginPasswordRecovery(new BeginPasswordRecoveryRequest { Login = "newuser" }, default);
        var verified = await h.Complete(pending, h.Emails.Last().Payload["confirmation_code"]);
        Assert.Null(verified.Session);
        Assert.NotNull(verified.SecurityProof);
        await h.Service.SetRecoveredPassword(new SetRecoveredPasswordRequest { SecurityProof = verified.SecurityProof, Password = "new-password123" }, default);
        var signIn = await h.Service.BeginSignIn(h.SignIn(password: "new-password123"), default);
        Assert.Equal(AuthChallengeState.Waiting, signIn.State);
        Assert.True(signIn.NeedsCode);
        await Assert.ThrowsAsync<RpcException>(() => h.Service.SetRecoveredPassword(new SetRecoveredPasswordRequest
        { SecurityProof = verified.SecurityProof, Password = "another-password" }, default));
    }

    [Fact]
    public async Task ExpiredRequest_CannotBeApprovedOrCompleted()
    {
        using var h = new Harness(); var pending = await h.Service.BeginRegistration(h.Registration(), default);
        await h.StartBot(pending); h.Clock.Advance(TimeSpan.FromMinutes(6)); await h.Approve();
        var result = await h.Complete(pending);
        Assert.Equal(AuthChallengeState.Expired, result.State);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task TelegramFailure_DoesNotIssueSession()
    {
        using var h = new Harness(); await h.Register(); h.Bot.FailDelivery = true;
        await Assert.ThrowsAsync<RpcException>(() => h.Service.BeginSignIn(h.SignIn(), default));
        Assert.Equal(1, h.RegisteredDevices);
    }

    [Fact]
    public async Task Resend_InvalidatesPreviousCode_AndKeepsOriginalDeadline()
    {
        using var h = new Harness(); await h.Register();
        var pending = await h.Service.BeginSignIn(h.SignIn(), default);
        var firstCode = h.Bot.LastCode;
        await Assert.ThrowsAsync<RpcException>(() => h.Service.Resend(pending.Challenge, default));
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var resent = await h.Service.Resend(pending.Challenge, default);
        Assert.Equal(pending.ExpiresAt, resent.ExpiresAt);
        Assert.Null((await h.Complete(pending, firstCode)).Session);
        Assert.NotNull((await h.Complete(pending, h.Bot.LastCode)).Session);
    }

    [Fact]
    public async Task BrowserSecret_IsRequired_AndIsNotTheBotToken()
    {
        using var h = new Harness(); var pending = await h.Service.BeginRegistration(h.Registration(), default);
        var forged = pending.Challenge.Clone(); forged.Secret = pending.TelegramUrl.Split("start=")[1];
        await Assert.ThrowsAsync<RpcException>(() => h.Service.Status(forged, default));
        await h.StartBot(pending); await h.Approve(); await h.Approve();
        Assert.NotNull((await h.Complete(pending)).Session);
        Assert.Equal(1, h.RegisteredDevices);
    }

    [Fact]
    public async Task RecoveryCodeRegeneration_ConsumesProofAndInvalidatesOldCodes()
    {
        using var h = new Harness(); var original = await h.Register();
        var pending = await h.Service.BeginReauthentication(new BeginReauthenticationRequest { Password = "password123" }, default);
        var proof = (await h.Complete(pending, h.Bot.LastCode)).SecurityProof;
        var replacement = await h.Service.GenerateRecoveryCodes(new SecurityProofRequest { SecurityProof = proof }, default);
        Assert.Equal(10, replacement.Codes.Count);
        await Assert.ThrowsAsync<RpcException>(() => h.Service.GenerateRecoveryCodes(new SecurityProofRequest { SecurityProof = proof }, default));
        var input = h.SignIn(); input.UseRecoveryCode = true;
        var login = await h.Service.BeginSignIn(input, default);
        Assert.Null((await h.Complete(login, original.RecoveryCodes[0], true)).Session);
        Assert.NotNull((await h.Complete(login, replacement.Codes[0], true)).Session);
    }

    private sealed class Harness : IDisposable
    {
        public IdentityContext Db { get; } = TestHelper.CreateContext();
        public FakeClock Clock { get; } = new();
        public FakeBot Bot { get; } = new();
        public List<EmailNotification> Emails { get; } = [];
        public AuthenticationService Service { get; }
        public AddDraftUserRequest? LastDraft { get; private set; }
        public int RegisteredDevices { get; private set; }
        private readonly Mock<UsersServerApi.UsersServerApiClient> _users = new();
        private bool _registered;

        public Harness(bool emailEnabled = false)
        {
            var user = new BarkFluff.Proto.Users.User { Id = 42, Username = "newuser" };
            _users.Setup(x => x.CheckExistUsernameAsync(It.IsAny<CheckExistUsernameRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Returns(() => Unary(new CheckExistResponse { Exist = _registered }));
            _users.Setup(x => x.AddDraftUserAsync(It.IsAny<AddDraftUserRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Callback<AddDraftUserRequest, Metadata, DateTime?, CancellationToken>((r, _, _, _) => LastDraft = r)
                .Returns(() => Unary(new AddDraftUserResponse { UserId = 42 }));
            _users.Setup(x => x.ConfirmUserAsync(It.IsAny<ConfirmUserRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Callback(() => _registered = true).Returns(() => Unary(new ConfirmUserResponse()));
            _users.Setup(x => x.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Returns(() => Unary(new FindByLoginResponse { User = user }));
            _users.Setup(x => x.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Returns(() => Unary(new GetByIdResponse { User = user }));
            _users.Setup(x => x.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Callback(() => RegisteredDevices++).Returns(() => Unary(new RegisterDeviceResponse()));
            _users.Setup(x => x.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, It.IsAny<CancellationToken>()))
                .Returns(() => Unary(new GetUserContactsResponse { User = user, Contact = new UserContact { Email = emailEnabled ? "verified@example.com" : "" } }));
            var publish = new Mock<IPublishEndpoint>();
            publish.Setup(x => x.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()))
                .Callback<EmailNotification, CancellationToken>((message, _) => Emails.Add(message)).Returns(Task.CompletedTask);
            var deviceId = Guid.NewGuid().ToString();
            var request = new RequestContext { DeviceId = deviceId, DeviceName = "Browser", AppName = "Web", AppVersion = "1", OperationSystem = "Test", TrustedIpAddress = "127.0.0.1" };
            Service = new AuthenticationService(Db, new AuthenticationStore(Db),
                new AuthenticationSecrets(new JwtSettings { SecretKey = "test-auth-secret" }), _users.Object, TestHelper.CreateJwtService(),
                new PasswordsStorage(Db), new NotificationQueueSender(publish.Object), Bot,
                new TelegramAuthOptions { Enabled = true, BotToken = "test-only-token" },
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Enabled"] = emailEnabled.ToString() }).Build(),
                request, TestHelper.CreateUserContext(42, deviceId), TestHelper.CreateAbuseGuard(), Clock);
        }

        public BeginRegistrationRequest Registration(AuthLoginMode mode = AuthLoginMode.PasswordSecondFactor) => new()
        { Username = "newuser", Password = "password123", FirstName = "New", LastName = "User", ConfirmationMethod = OtpTypeId.Telegram, LoginMode = mode };
        public BeginSignInRequest SignIn(string password = "password123") => new()
        { Login = "newuser", Password = password, LoginMode = AuthLoginMode.PasswordSecondFactor, Factor = OtpTypeId.Telegram };
        public async Task<CompleteAuthChallengeResponse> Register(AuthLoginMode mode = AuthLoginMode.PasswordSecondFactor)
        {
            var pending = await Service.BeginRegistration(Registration(mode), default);
            await StartBot(pending); await Approve(); return await Complete(pending);
        }
        public Task StartBot(AuthChallengeResponse pending) => Service.ProcessTelegramUpdate(JsonSerializer.SerializeToElement(new
        { message = new { text = "/start " + pending.TelegramUrl.Split("start=")[1], from = new { id = 123L, username = "owner", is_bot = false }, chat = new { id = 123L, type = "private" } } }), default);
        public Task Approve(long actor = 123) => Service.ProcessTelegramUpdate(JsonSerializer.SerializeToElement(new
        { callback_query = new { id = "callback", data = "yes:" + Bot.Messages.Last(x => x.Token != null).Token, from = new { id = actor, is_bot = false }, message = new { chat = new { id = actor, type = "private" } } } }), default);
        public Task<CompleteAuthChallengeResponse> Complete(AuthChallengeResponse c, string code = "", bool recovery = false) =>
            Service.Complete(new CompleteAuthChallengeRequest { Challenge = c.Challenge, Code = code, UseRecoveryCode = recovery }, default);
        public void Dispose() => Db.Dispose();
    }

    private static AsyncUnaryCall<T> Unary<T>(T result) => new(Task.FromResult(result), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
    private sealed class FakeBot : ITelegramAuthBot
    {
        public List<(long Chat, string Text, string? Token)> Messages { get; } = [];
        public bool FailDelivery { get; set; }
        public string LastCode => Regex.Match(Messages.Last().Text, @"Код: (\d{6})").Groups[1].Value;
        public Task<TelegramBotIdentity> GetIdentity(CancellationToken ct) => Task.FromResult(new TelegramBotIdentity(1, "test_bot"));
        public Task<JsonElement[]> GetUpdates(long offset, CancellationToken ct) => Task.FromResult(Array.Empty<JsonElement>());
        public Task Send(long chatId, string text, string? approvalToken, CancellationToken ct)
        {
            if (FailDelivery) throw new RpcException(new Status(StatusCode.Unavailable, "Telegram unavailable"));
            Messages.Add((chatId, text, approvalToken)); return Task.CompletedTask;
        }
        public Task Answer(string callbackId, string text, CancellationToken ct) => Task.CompletedTask;
    }
}
