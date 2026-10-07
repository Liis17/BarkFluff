using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Identity;
using BarkFluff.Shared.Queue.Notifications;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService(
    IdentityContext db, AuthenticationStore store, AuthenticationSecrets secrets,
    UsersServerApi.UsersServerApiClient users, JwtService jwt, PasswordsStorage passwords,
    NotificationQueueSender notifications, LoginNotificationService loginNotifications,
    ITelegramAuthBot bot, TelegramAuthOptions telegram,
    IConfiguration configuration, RequestContext request, UserContext currentUser,
    IIdentityAbuseGuard guard, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool EmailAvailable => configuration.GetValue("Email:Enabled", true);
    public void RequireEmailDelivery()
    {
        if (!EmailAvailable) throw Unavailable("Email is disabled; use Telegram registration in the web app");
    }
    private static RpcException Invalid(string text) => new(new Status(StatusCode.InvalidArgument, text));
    private static RpcException Unavailable(string text) => new(new Status(StatusCode.FailedPrecondition, text));
    private static RpcException Denied() => new(new Status(StatusCode.PermissionDenied, "Invalid or expired confirmation"));

    private Task<AuthUserProperty?> Settings(long userId, CancellationToken ct) =>
        db.AuthUserProperties.SingleOrDefaultAsync(x => x.UserId == userId, ct);

    private async Task<AuthUserProperty> EnsureSettings(long userId, CancellationToken ct)
    {
        var settings = await Settings(userId, ct);
        if (settings != null) return settings;
        settings = new AuthUserProperty { UserId = userId, LoginMode = AuthLoginMode.Password };
        db.AuthUserProperties.Add(settings);
        return settings;
    }

    private IEnumerable<OtpTypeId> AvailableFactors(AuthUserProperty? settings) =>
        new[] { OtpTypeId.Authenticator, OtpTypeId.Email, OtpTypeId.Telegram }
            .Where(f => AuthenticationPolicy.FactorEnabled(settings, f) &&
                (f != OtpTypeId.Email || EmailAvailable) &&
                (f != OtpTypeId.Telegram || telegram.Configured));

    private OtpTypeId PreferredAvailableFactor(AuthUserProperty? settings)
    {
        var preferred = (OtpTypeId)(settings?.PreferredFactor ?? BarkFluff.Identity.Domain.OtpType.Unknown);
        var available = AvailableFactors(settings).ToArray();
        return available.Contains(preferred) ? preferred : available.FirstOrDefault();
    }

    private string DeviceId()
    {
        if (!Guid.TryParse(request.DeviceId, out var id) || string.IsNullOrWhiteSpace(request.DeviceName) ||
            string.IsNullOrWhiteSpace(request.OperationSystem) || string.IsNullOrWhiteSpace(request.AppName) ||
            string.IsNullOrWhiteSpace(request.AppVersion))
            throw Invalid("Device ID, name, operating system and app version are required");
        return id.ToString();
    }

    private long AuthenticatedUser()
    {
        if (currentUser.TokenType != TokenType.User || currentUser.UserId == 0 || currentUser.DeviceId != DeviceId())
            throw Denied();
        return currentUser.UserId;
    }

    private async Task GuardRequest(string subject, bool sending, CancellationToken ct)
    {
        await guard.EnsureRequestAllowedAsync(IdentityAbuseOperation.Auth, request.TrustedIpAddress, subject, sending, ct);
    }

    public async Task<GetAuthCapabilitiesResponse> Capabilities(CancellationToken ct)
    {
        var result = new GetAuthCapabilitiesResponse { EmailAvailable = EmailAvailable };
        if (telegram.Configured)
        {
            try
            {
                result.TelegramBotUsername = (await bot.GetIdentity(ct)).Username;
                result.TelegramAvailable = true;
            }
            catch (RpcException) { /* A disabled channel must never become an authentication fallback. */ }
        }
        return result;
    }

    private (AuthenticationChallenge Challenge, AuthChallengeReference Reference) NewChallenge(
        AuthenticationPurpose purpose, long userId, string username, AuthUserProperty? settings)
    {
        var secret = AuthenticationSecrets.NewSecret();
        var challenge = new AuthenticationChallenge
        {
            Id = Guid.NewGuid(), Purpose = purpose, UserId = userId, Username = username,
            SecretHash = secrets.Hash(secret), CreatedAt = Now, ExpiresAt = Now.AddMinutes(5),
            PolicyVersion = settings?.PolicyVersion ?? 0, LoginMode = AuthenticationPolicy.Mode(settings),
            DeviceId = DeviceId(), DeviceName = request.DeviceName!, OperationSystem = request.OperationSystem!,
            AppName = $"{request.AppName} v.{request.AppVersion}", IpAddress = request.TrustedIpAddress ?? ""
        };
        return (challenge, new AuthChallengeReference { Id = challenge.Id.ToString(), Secret = secret });
    }

    private async Task<AuthChallengeResponse> Describe(AuthenticationChallenge challenge, AuthChallengeReference reference, CancellationToken ct)
    {
        var response = new AuthChallengeResponse
        {
            Challenge = reference, State = challenge.State, Factor = challenge.Factor,
            ExpiresAt = Timestamp.FromDateTime(challenge.ExpiresAt),
            NeedsCode = challenge.UseRecoveryCode || challenge.Factor == OtpTypeId.Authenticator || challenge.CodeHash != null
        };
        var settings = challenge.UserId == 0 ? null : await Settings(challenge.UserId, ct);
        response.AvailableFactors.AddRange(AvailableFactors(settings));
        return response;
    }

    private async Task<AuthenticationChallenge> Load(AuthChallengeReference reference, CancellationToken ct)
    {
        if (reference == null || !Guid.TryParse(reference.Id, out var id) || reference.Secret.Length != 48) throw Denied();
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (challenge == null || !secrets.Matches(challenge.SecretHash, reference.Secret) || challenge.DeviceId != DeviceId()) throw Denied();
        return challenge;
    }

    private async Task<T> WithChallenge<T>(AuthChallengeReference reference, Func<AuthenticationChallenge, Task<T>> action, CancellationToken ct)
    {
        var initial = await Load(reference, ct);
        var key = initial.UserId != 0 ? initial.UserId : BitConverter.ToInt64(initial.Id.ToByteArray());
        return await store.ForUser(key, async () =>
        {
            if (db.Database.IsNpgsql())
                await db.AuthenticationChallenges.FromSqlInterpolated($"SELECT * FROM \"AuthenticationChallenges\" WHERE \"Id\" = {initial.Id} FOR UPDATE").LoadAsync(ct);
            var challenge = await Load(reference, ct);
            await RefreshState(challenge, ct);
            return await action(challenge);
        }, ct);
    }

    private async Task RefreshState(AuthenticationChallenge challenge, CancellationToken ct)
    {
        if (challenge.State is AuthChallengeState.Waiting or AuthChallengeState.Approved or AuthChallengeState.Completed)
        {
            if (challenge.ExpiresAt <= Now) challenge.State = AuthChallengeState.Expired;
            if (challenge.UserId != 0 && challenge.Purpose != AuthenticationPurpose.Registration)
            {
                var settings = await Settings(challenge.UserId, ct);
                if ((settings?.PolicyVersion ?? 0) != challenge.PolicyVersion)
                    challenge.State = AuthChallengeState.Cancelled;
            }
        }
    }

    private string OperationDetailsText(AuthenticationChallenge c) =>
        $"👤 Аккаунт: {c.Username}\n🧭 Действие: {c.Purpose switch {
            AuthenticationPurpose.Registration => "Регистрация", AuthenticationPurpose.TelegramBinding => "Привязка Telegram",
            AuthenticationPurpose.Reauthentication => "Изменение настроек безопасности", AuthenticationPurpose.FastAuth => "Вход по QR-коду",
            AuthenticationPurpose.PasswordRecovery => "Восстановление пароля", _ => "Вход в аккаунт" }}\n" +
        $"💻 Устройство: {c.DeviceName} · {c.OperationSystem}\n📱 Приложение: {c.AppName}\n🌐 IP: {c.IpAddress}\n🕒 Время: {Now:u}";

    private string OperationText(AuthenticationChallenge c) => $"🔐 BarkFluff · {telegram.NodeName}\n" +
        OperationDetailsText(c) + "\n\n⚠️ Подтверждайте только действие, которое вы начали сами.";

    private async Task<string> Deliver(AuthenticationChallenge c, CancellationToken ct)
    {
        if (c.UseRecoveryCode || c.Factor == OtpTypeId.Authenticator) return "";
        if (c.SentAt.HasValue && c.SentAt.Value.AddMinutes(1) > Now) throw new RpcException(new Status(StatusCode.ResourceExhausted, "Wait before resending"));
        c.SentAt = Now;
        if (c.Factor == OtpTypeId.Telegram)
        {
            if (!telegram.Configured) throw Unavailable("Telegram is unavailable");
            var me = await bot.GetIdentity(ct);
            if (c.Purpose is AuthenticationPurpose.Registration or AuthenticationPurpose.TelegramBinding)
            {
                var startToken = AuthenticationSecrets.NewSecret();
                c.TelegramTokenHash = secrets.Hash(startToken);
                c.TelegramId = null;
                return $"https://t.me/{me.Username}?start={startToken}";
            }
            if (!c.TelegramId.HasValue) throw Unavailable("Telegram is not linked");
            if (c.LoginMode == AuthLoginMode.TelegramLogin || c.Purpose == AuthenticationPurpose.FastAuth)
            {
                var callbackToken = AuthenticationSecrets.NewSecret();
                c.TelegramTokenHash = secrets.Hash(callbackToken);
                await bot.Send(c.TelegramId.Value, OperationText(c), callbackToken, ct);
                return "";
            }
        }
        string code;
        do { code = CodeGenerator.GenerateDigitalCode(6); }
        while (c.CodeHash != null && secrets.Matches(c.CodeHash, $"{c.Id}:{code}"));
        c.CodeHash = secrets.Hash($"{c.Id}:{code}");
        if (c.Factor == OtpTypeId.Telegram)
            await bot.SendCode(c.TelegramId!.Value,
                $"🔐 Код подтверждения · BarkFluff · {telegram.NodeName}", code,
                OperationDetailsText(c) + "\n⏳ Действует 5 минут.\n\n⚠️ Подтверждайте только действие, которое вы начали сами.", ct);
        else if (c.Factor == OtpTypeId.Email)
        {
            if (!EmailAvailable || string.IsNullOrWhiteSpace(c.Email)) throw Unavailable("Email is unavailable");
            await notifications.SendNotification(new EmailNotification
            {
                OwnerId = c.UserId, Address = c.Email, CreatedAt = Now, ServiceId = ServiceId.Identity,
                Title = "BarkFluff: код подтверждения", Type = NotificationType.ConfirmationAuth,
                Payload = new Dictionary<string, string> { ["confirmation_code"] = code, ["username"] = c.Username,
                    ["ip"] = c.IpAddress, ["devicename"] = c.DeviceName, ["os"] = c.OperationSystem,
                    ["appname"] = c.AppName, ["datetime"] = Now.ToString("u"), ["location"] = "" }
            });
        }
        return "";
    }

    public async Task<AuthChallengeResponse> Status(AuthChallengeReference reference, CancellationToken ct)
    {
        if (!Guid.TryParse(reference.Id, out var id)) throw Denied();
        await guard.EnsureChallengeReadAllowedAsync(id, request.TrustedIpAddress, ct);
        return await WithChallenge(reference, c => Describe(c, reference, ct), ct);
    }

    public async Task<AuthChallengeResponse> Cancel(AuthChallengeReference reference, CancellationToken ct)
    {
        await GuardRequest(reference.Id, false, ct);
        return await WithChallenge(reference, async c =>
        {
            if (c.State is AuthChallengeState.Waiting or AuthChallengeState.Approved) c.State = AuthChallengeState.Cancelled;
            return await Describe(c, reference, ct);
        }, ct);
    }

    public async Task<AuthChallengeResponse> Resend(AuthChallengeReference reference, CancellationToken ct)
    {
        await GuardRequest(reference.Id, true, ct);
        return await WithChallenge(reference, async c =>
        {
            if (c.State != AuthChallengeState.Waiting) throw Denied();
            if (c.Purpose == AuthenticationPurpose.SignIn && c.UserId == 0 && c.LoginMode == AuthLoginMode.TelegramLogin)
                return await Describe(c, reference, ct);
            var url = await Deliver(c, ct);
            var response = await Describe(c, reference, ct);
            response.TelegramUrl = url;
            return response;
        }, ct);
    }
}
