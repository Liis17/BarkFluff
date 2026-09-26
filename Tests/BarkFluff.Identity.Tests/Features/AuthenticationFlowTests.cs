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
using Microsoft.EntityFrameworkCore;
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
    public async Task TelegramApproval_UsesEmojisAndEditsOriginalMessageAfterApproval()
    {
        using var h = new Harness();
        var pending = await h.Service.BeginRegistration(h.Registration(), default);
        await h.StartBot(pending);

        var message = h.Bot.Messages.Last(x => x.Token != null);
        Assert.Contains("🔐", message.Text);
        Assert.Contains("👤 Аккаунт:", message.Text);

        await h.Approve(999);
        Assert.Empty(h.Bot.Edits);

        await h.Approve();

        var edit = Assert.Single(h.Bot.Edits);
        Assert.Equal(123, edit.Chat);
        Assert.Equal(456, edit.MessageId);
        Assert.EndsWith("\n\n✅ Запрос подтверждён.", edit.Text);
    }

    [Fact]
    public async Task TelegramRejection_EditsOriginalMessageWithRejectedStatus()
    {
        using var h = new Harness();
        var pending = await h.Service.BeginRegistration(h.Registration(), default);
        await h.StartBot(pending);

        await h.Reject();

        var edit = Assert.Single(h.Bot.Edits);
        Assert.EndsWith("\n\n❌ Запрос отклонён.", edit.Text);
        Assert.Equal(AuthChallengeState.Rejected, (await h.Service.Status(pending.Challenge, default)).State);
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
    public async Task CorrectPasswordWithDisabledSignInMode_ReturnsTypedErrorWithoutCreatingChallenge()
    {
        using var h = new Harness();
        await h.Register();
        var challengeCount = await h.Db.AuthenticationChallenges.CountAsync();
        var messageCount = h.Bot.Messages.Count;
        var request = h.SignIn();
        request.LoginMode = AuthLoginMode.Password;

        var response = await h.Service.BeginSignIn(request, default);

        Assert.Equal("login_mode_disabled", response.ErrorCode);
        Assert.Equal(challengeCount, await h.Db.AuthenticationChallenges.CountAsync());
        Assert.Equal(messageCount, h.Bot.Messages.Count);
    }

    [Fact]
    public async Task WrongPasswordWithDisabledSignInMode_DoesNotRevealConfiguredMode()
    {
        using var h = new Harness();
        await h.Register();
        var request = h.SignIn(password: "incorrect");
        request.LoginMode = AuthLoginMode.Password;

        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => h.Service.BeginSignIn(request, default));
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
    public async Task PreferredEmailUnavailableOnNode_FallsBackToEnabledTelegramFactor()
    {
        using var h = new Harness(); await h.Register();
        var settings = await h.Db.AuthUserProperties.SingleAsync(x => x.UserId == 42);
        settings.EmailOtpEnabled = true;
        settings.PreferredFactor = BarkFluff.Identity.Domain.OtpType.Email;
        await h.Db.SaveChangesAsync();

        Assert.Equal(OtpTypeId.Telegram, (await h.Service.GetSecuritySettings(default)).PreferredFactor);
        var input = h.SignIn(); input.Factor = OtpTypeId.Unknown;
        var pending = await h.Service.BeginSignIn(input, default);
        Assert.Equal(OtpTypeId.Telegram, pending.Factor);
        Assert.True(pending.NeedsCode);
    }

    [Fact]
    public async Task DisablingAuthenticator_PrefersTelegramWhenEmailIsDisabledOnNode()
    {
        using var h = new Harness(); await h.Register();
        var settings = await h.Db.AuthUserProperties.SingleAsync(x => x.UserId == 42);
        settings.OtpEnabled = true;
        settings.EmailOtpEnabled = true;
        settings.PreferredFactor = BarkFluff.Identity.Domain.OtpType.Email;
        await h.Db.SaveChangesAsync();

        var reauthentication = await h.Service.BeginReauthentication(new BeginReauthenticationRequest
        { Password = "password123", Factor = OtpTypeId.Telegram }, default);
        var proof = (await h.Complete(reauthentication, h.Bot.LastCode)).SecurityProof;
        await h.Service.DisableFactor(new DisableOtpVerificationRequest
        { OtpType = OtpTypeId.Authenticator, SecurityProof = proof }, default);

        Assert.Equal(BarkFluff.Identity.Domain.OtpType.Telegram,
            (await h.Db.AuthUserProperties.SingleAsync(x => x.UserId == 42)).PreferredFactor);
        var signIn = h.SignIn(); signIn.Factor = OtpTypeId.Unknown;
        Assert.Equal(OtpTypeId.Telegram, (await h.Service.BeginSignIn(signIn, default)).Factor);
    }

    [Fact]
    public async Task RecoveryCodeSignIn_DoesNotSendAnotherFactorCode()
    {
        using var h = new Harness(); var registered = await h.Register();
        var messageCount = h.Bot.Messages.Count;
        var request = h.SignIn(); request.UseRecoveryCode = true;
        var pending = await h.Service.BeginSignIn(request, default);
        Assert.Equal(messageCount, h.Bot.Messages.Count);
        Assert.True(pending.NeedsCode);
        Assert.NotNull((await h.Complete(pending, registered.RecoveryCodes[0], recovery: true)).Session);
    }

    [Fact]
    public async Task RemovingAuthenticatorCannotLeaveOnlyEmailDisabledOnNode()
    {
        using var h = new Harness();
        h.Db.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 42, LoginMode = AuthLoginMode.PasswordSecondFactor,
            OtpEnabled = true, EmailOtpEnabled = true
        });
        await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RpcException>(() => h.Service.ValidateFactorRemoval(OtpTypeId.Authenticator, default));
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
        var beforeSignIn = h.Bot.Messages.Count;
        var pending = await h.Service.BeginSignIn(new BeginSignInRequest { Login = "newuser", LoginMode = AuthLoginMode.TelegramLogin }, default);
        var approval = Assert.Single(h.Bot.Messages.Skip(beforeSignIn));
        Assert.Equal(123, approval.Chat);
        Assert.NotNull(approval.Token);
        Assert.False(pending.NeedsCode);
        await h.Approve(999);
        Assert.Equal(AuthChallengeState.Waiting, (await h.Service.Status(pending.Challenge, default)).State);
        await h.Approve();
        Assert.NotNull((await h.Complete(pending)).Session);
        Assert.Equal("login_mode_disabled", (await h.Service.BeginSignIn(h.SignIn(), default)).ErrorCode);
    }

    [Fact]
    public async Task TelegramButtonLogin_WithDifferentAccountModeReturnsGenericPendingChallengeWithoutNotification()
    {
        using var h = new Harness(); await h.Register();
        var beforeSignIn = h.Bot.Messages.Count;

        var pending = await h.Service.BeginSignIn(new BeginSignInRequest
        { Login = "newuser", LoginMode = AuthLoginMode.TelegramLogin }, default);

        Assert.Equal(AuthChallengeState.Waiting, pending.State);
        Assert.False(pending.NeedsCode);
        var resent = await h.Service.Resend(pending.Challenge, default);
        Assert.Equal(AuthChallengeState.Waiting, resent.State);
        Assert.Equal(beforeSignIn, h.Bot.Messages.Count);
    }

    [Fact]
    public async Task TelegramSecondFactor_SendsCodeAfterTitleAndBeforeRequestDetails()
    {
        using var h = new Harness(); await h.Register();

        await h.Service.BeginSignIn(h.SignIn(), default);

        var message = h.Bot.Messages.Last(x => x.Token == null);
        Assert.StartsWith("<b>🔐 Код подтверждения ·", message.Text);
        Assert.Matches(@"^<b>[^<]*</b>\n\n<code>\d{6}</code>\n\n👤 Аккаунт: newuser", message.Text);
        Assert.Contains("🧭 Действие: Вход в аккаунт", message.Text);
        Assert.Contains("⏳ Действует 5 минут.", message.Text);
    }

    [Fact]
    public async Task PasswordOnlyMode_CompletesWithoutSecondFactor()
    {
        using var h = new Harness(); await h.Register(AuthLoginMode.Password);
        var request = h.SignIn(); request.LoginMode = AuthLoginMode.Password;
        var pending = await h.Service.BeginSignIn(request, default);
        Assert.Equal(AuthChallengeState.Approved, pending.State);
        Assert.NotNull((await h.Complete(pending)).Session);
    }

    [Fact]
    public async Task EmailSecondFactor_UsesVerifiedRegistrationAddress()
    {
        using var h = new Harness(emailEnabled: true);
        var registration = h.Registration(); registration.ConfirmationMethod = OtpTypeId.Email; registration.Email = "owner@example.com";
        var pendingRegistration = await h.Service.BeginRegistration(registration, default);
        var registrationCode = h.Emails.Last().Payload["confirmation_code"];
        Assert.NotNull((await h.Complete(pendingRegistration, registrationCode)).Session);

        var input = h.SignIn(); input.Factor = OtpTypeId.Unknown;
        var pending = await h.Service.BeginSignIn(input, default);
        Assert.Equal(OtpTypeId.Email, pending.Factor);
        var emailCode = h.Emails.Last(x => x.Type == NotificationType.ConfirmationAuth).Payload["confirmation_code"];
        Assert.NotNull((await h.Complete(pending, emailCode)).Session);
    }

    [Fact]
    public async Task AuthenticatorSecondFactor_UsesTotpChallenge()
    {
        using var h = new Harness(); await h.Register();
        var settings = await h.Db.AuthUserProperties.SingleAsync(x => x.UserId == 42);
        settings.OtpEnabled = true;
        settings.OtpSecret = OtpNet.Base32Encoding.ToString(OtpNet.KeyGeneration.GenerateRandomKey(20));
        settings.PreferredFactor = BarkFluff.Identity.Domain.OtpType.Authenticator;
        await h.Db.SaveChangesAsync();

        var input = h.SignIn(); input.Factor = OtpTypeId.Unknown;
        var pending = await h.Service.BeginSignIn(input, default);
        Assert.Equal(OtpTypeId.Authenticator, pending.Factor);
        var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(settings.OtpSecret))
            .ComputeTotp(h.Clock.GetUtcNow().UtcDateTime);
        Assert.NotNull((await h.Complete(pending, code)).Session);
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
        await h.StartBot(pending); h.Bot.FailAnswer = true; await h.Approve(); await h.Approve();
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

    [Fact]
    public async Task FastAuth_RequiresTelegram_AndRepeatedApprovalReturnsSameSession()
    {
        using var h = new Harness(); await h.Register();
        var input = new CreateSessionForUserServerRequest { UserId = 42, DeviceId = Guid.NewGuid().ToString(),
            AttemptId = Guid.NewGuid().ToString(), ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5)),
            DeviceName = "New browser", OperationSystem = "Test", AppName = "Web" };
        var pending = await h.Service.CreateFastAuthSession(input, default);
        Assert.Equal(AuthChallengeState.Waiting, pending.ConfirmationState);
        Assert.Null(pending.AccessToken);
        var count = h.Bot.Messages.Count;
        Assert.Equal(AuthChallengeState.Waiting, (await h.Service.CreateFastAuthSession(input, default)).ConfirmationState);
        Assert.Equal(count, h.Bot.Messages.Count);
        await h.Approve();
        var completed = await h.Service.CreateFastAuthSession(input, default);
        var repeat = await h.Service.CreateFastAuthSession(input, default);
        Assert.Equal(AuthChallengeState.Completed, completed.ConfirmationState);
        Assert.Equal(completed.RefreshToken.Value, repeat.RefreshToken.Value);
        Assert.Equal(2, h.RegisteredDevices);
        input.AttemptId = "";
        await Assert.ThrowsAsync<RpcException>(() => h.Service.CreateFastAuthSession(input, default));
    }

    [Fact]
    public async Task FastAuth_ExpiredApprovalCannotCreateSession()
    {
        using var h = new Harness(); await h.Register();
        var input = new CreateSessionForUserServerRequest { UserId = 42, DeviceId = Guid.NewGuid().ToString(),
            AttemptId = Guid.NewGuid().ToString(), ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(h.Clock.GetUtcNow().UtcDateTime.AddSeconds(20)),
            DeviceName = "New browser", OperationSystem = "Test", AppName = "Web" };
        await h.Service.CreateFastAuthSession(input, default);
        h.Clock.Advance(TimeSpan.FromSeconds(21)); await h.Approve();
        var result = await h.Service.CreateFastAuthSession(input, default);
        Assert.Equal(AuthChallengeState.Expired, result.ConfirmationState);
        Assert.Null(result.AccessToken);
        Assert.Equal(1, h.RegisteredDevices);
    }

    [Fact]
    public async Task ParallelCompletionAcrossInstances_ReturnsOneRefreshSession()
    {
        using var h = new Harness(); await h.Register();
        var pending = await h.Service.BeginSignIn(h.SignIn(), default);
        var code = h.Bot.LastCode;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = new IdentityContext(h.Options);
            return await h.CreateService(context).Complete(new CompleteAuthChallengeRequest { Challenge = pending.Challenge, Code = code }, default);
        }));
        Assert.Single(results.Select(x => x.Session.RefreshToken.Value).Distinct());
        Assert.Equal(2, h.RegisteredDevices);
    }

    [Fact]
    public async Task ParallelRecoveryCodeAcrossInstances_IsConsumedOnlyOnce()
    {
        using var h = new Harness(); var registered = await h.Register();
        var request = h.SignIn(); request.UseRecoveryCode = true;
        var first = await h.Service.BeginSignIn(request, default);
        var second = await h.Service.BeginSignIn(request, default);
        var results = await Task.WhenAll(new[] { first, second }.Select(async pending =>
        {
            await using var context = new IdentityContext(h.Options);
            return await h.CreateService(context).Complete(new CompleteAuthChallengeRequest
            { Challenge = pending.Challenge, Code = registered.RecoveryCodes[0], UseRecoveryCode = true }, default);
        }));
        Assert.Single(results, x => x.Session != null);
    }

    [Fact]
    public async Task OtpRequiredException_CommitsEmailChallengeState()
    {
        using var h = new Harness();
        var challengeId = Guid.NewGuid();
        var store = new AuthenticationStore(h.Db);
        await Assert.ThrowsAsync<OtpCodeNeedException>(() => store.ForUserPreserving<bool>(42, async () =>
        {
            h.Db.AuthenticationChallenges.Add(new AuthenticationChallenge
            {
                Id = challengeId, Purpose = AuthenticationPurpose.SignIn, UserId = 42,
                CreatedAt = h.Clock.GetUtcNow().UtcDateTime, ExpiresAt = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5)
            });
            await Task.Yield();
            throw new OtpCodeNeedException();
        }, default, typeof(OtpCodeNeedException)));

        h.Db.ChangeTracker.Clear();
        Assert.True(await h.Db.AuthenticationChallenges.AnyAsync(x => x.Id == challengeId));
    }

    private sealed class Harness : IDisposable
    {
        public DbContextOptions<IdentityContext> Options { get; }
        public IdentityContext Db { get; }
        public Func<IdentityContext, AuthenticationService> CreateService { get; private set; } = null!;
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
            var connection = Environment.GetEnvironmentVariable("BARKFLUFF_TEST_POSTGRES");
            var builder = new DbContextOptionsBuilder<IdentityContext>();
            if (connection != null)
            {
                var value = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Database = "bf_auth_" + Guid.NewGuid().ToString("N") };
                builder.UseNpgsql(value.ConnectionString);
            }
            else builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            Options = builder.Options; Db = new IdentityContext(Options);
            if (Db.Database.IsNpgsql()) Db.Database.Migrate();
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
            CreateService = context => new AuthenticationService(context, new AuthenticationStore(context),
                new AuthenticationSecrets(new JwtSettings { SecretKey = "test-auth-secret" }), _users.Object, TestHelper.CreateJwtService(),
                new PasswordsStorage(context), new NotificationQueueSender(publish.Object), Bot,
                new TelegramAuthOptions { Enabled = true, BotToken = "test-only-token" },
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Enabled"] = emailEnabled.ToString() }).Build(),
                request, TestHelper.CreateUserContext(42, deviceId), TestHelper.CreateAbuseGuard(), Clock);
            Service = CreateService(Db);
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
        public Task Approve(long actor = 123) => Respond(true, actor);
        public Task Reject(long actor = 123) => Respond(false, actor);
        private Task Respond(bool approve, long actor)
        {
            var message = Bot.Messages.Last(x => x.Token != null);
            return Service.ProcessTelegramUpdate(JsonSerializer.SerializeToElement(new
            { callback_query = new { id = "callback", data = (approve ? "yes:" : "no:") + message.Token, from = new { id = actor, is_bot = false }, message = new { message_id = 456L, text = message.Text, chat = new { id = actor, type = "private" } } } }), default);
        }
        public Task<CompleteAuthChallengeResponse> Complete(AuthChallengeResponse c, string code = "", bool recovery = false) =>
            Service.Complete(new CompleteAuthChallengeRequest { Challenge = c.Challenge, Code = code, UseRecoveryCode = recovery }, default);
        public void Dispose() { if (Db.Database.IsNpgsql()) Db.Database.EnsureDeleted(); Db.Dispose(); }
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
        public List<(long Chat, long MessageId, string Text)> Edits { get; } = [];
        public bool FailDelivery { get; set; }
        public bool FailAnswer { get; set; }
        public string LastCode => Regex.Match(Messages.Last().Text, @"<code>(\d{6})</code>").Groups[1].Value;
        public Task<TelegramBotIdentity> GetIdentity(CancellationToken ct) => Task.FromResult(new TelegramBotIdentity(1, "test_bot"));
        public Task<JsonElement[]> GetUpdates(long offset, CancellationToken ct) => Task.FromResult(Array.Empty<JsonElement>());
        public Task Send(long chatId, string text, string? approvalToken, CancellationToken ct)
        {
            if (FailDelivery) throw new RpcException(new Status(StatusCode.Unavailable, "Telegram unavailable"));
            Messages.Add((chatId, text, approvalToken)); return Task.CompletedTask;
        }
        public Task SendCode(long chatId, string title, string code, string details, CancellationToken ct)
        {
            if (FailDelivery) throw new RpcException(new Status(StatusCode.Unavailable, "Telegram unavailable"));
            Messages.Add((chatId, $"<b>{title}</b>\n\n<code>{code}</code>\n\n{details}", null));
            return Task.CompletedTask;
        }
        public Task Answer(string callbackId, string text, CancellationToken ct) => FailAnswer
            ? Task.FromException(new RpcException(new Status(StatusCode.Unavailable, "Callback is too old"))) : Task.CompletedTask;
        public Task EditMessage(long chatId, long messageId, string text, CancellationToken ct)
        {
            Edits.Add((chatId, messageId, text));
            return Task.CompletedTask;
        }
    }
}
