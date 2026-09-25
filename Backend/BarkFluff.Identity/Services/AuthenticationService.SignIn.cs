using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using OtpType = BarkFluff.Identity.Domain.OtpType;
using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService
{
    private async Task<User?> FindUser(string login, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 254) throw Invalid("Login is required");
        var query = new FindByLoginRequest();
        if (login.Contains('@')) query.Email = login.Trim(); else query.Username = login.Trim();
        try
        {
            var response = await users.FindByLoginAsync(query, cancellationToken: ct);
            return response.User?.IsBot == false ? response.User : null;
        }
        catch (UserNotFoundException) { return null; }
    }

    private async Task VerifyPassword(long userId, string login, string password, CancellationToken ct)
    {
        await guard.EnsureLoginAllowedAsync(login, request.TrustedIpAddress, ct);
        if (userId != 0) await guard.EnsureUserAllowedAsync(userId, ct);
        var hash = userId == 0 ? null : await passwords.GetUserPasswordHash(userId);
        if (string.IsNullOrEmpty(password) || !PasswordHasher.VerifyPassword(password, hash))
        {
            var failure = await guard.RegisterLoginFailureAsync(login, request.TrustedIpAddress, userId == 0 ? null : userId, ct);
            await guard.DelayAfterFailureAsync(failure.Attempts, ct);
            throw new InvalidLoginOrPasswordException();
        }
    }

    public async Task<AuthChallengeResponse> BeginRegistration(BeginRegistrationRequest input, CancellationToken ct)
    {
        ValidatePassword(input.Password);
        DeviceId();
        await GuardRequest(input.Username, true, ct);
        if (input.ConfirmationMethod is not (OtpTypeId.Email or OtpTypeId.Telegram)) throw Invalid("Choose email or Telegram");
        if (input.ConfirmationMethod == OtpTypeId.Email && (!EmailAvailable || !ValidEmail(input.Email))) throw Invalid("A valid email is required");
        if (input.ConfirmationMethod == OtpTypeId.Telegram && !telegram.Configured) throw Unavailable("Telegram is unavailable");
        var mode = input.LoginMode == AuthLoginMode.LoginModeUnspecified
            ? (input.ConfirmationMethod == OtpTypeId.Telegram ? AuthLoginMode.PasswordSecondFactor : AuthLoginMode.Password)
            : input.LoginMode;
        if (mode is not (AuthLoginMode.Password or AuthLoginMode.PasswordSecondFactor or AuthLoginMode.TelegramLogin) ||
            (mode == AuthLoginMode.TelegramLogin && input.ConfirmationMethod != OtpTypeId.Telegram)) throw Invalid("Invalid login mode");
        var exists = await users.CheckExistUsernameAsync(new CheckExistUsernameRequest { Username = input.Username.Trim() }, cancellationToken: ct);
        if (exists.Exist) throw new UsernameExistException();

        var (c, reference) = NewChallenge(AuthenticationPurpose.Registration, 0, input.Username.Trim(), null);
        c.PasswordHash = PasswordHasher.HashPassword(input.Password);
        c.FirstName = input.FirstName.Trim(); c.LastName = input.LastName.Trim();
        c.Email = input.ConfirmationMethod == OtpTypeId.Email ? input.Email.Trim() : null;
        c.Factor = input.ConfirmationMethod; c.LoginMode = mode;
        var url = await Deliver(c, ct);
        db.AuthenticationChallenges.Add(c);
        await db.SaveChangesAsync(ct);
        var response = await Describe(c, reference, ct); response.TelegramUrl = url;
        return response;
    }

    public async Task<AuthChallengeResponse> BeginSignIn(BeginSignInRequest input, CancellationToken ct)
    {
        DeviceId();
        await GuardRequest(input.Login, true, ct);
        var user = await FindUser(input.Login, ct);
        return await store.ForUser(user?.Id ?? 0, async () =>
        {
            var settings = user == null ? null : await Settings(user.Id, ct);
            if (input.LoginMode != AuthLoginMode.TelegramLogin)
                await VerifyPassword(user?.Id ?? 0, input.Login, input.Password, ct);
            else if (user != null) await guard.EnsureUserAllowedAsync(user.Id, ct);

            var (c, reference) = NewChallenge(AuthenticationPurpose.SignIn, user?.Id ?? 0, user?.Username ?? input.Login, settings);
            // Identical waiting response for an unknown/unavailable Telegram login.
            if (input.LoginMode == AuthLoginMode.TelegramLogin &&
                (user == null || AuthenticationPolicy.Mode(settings) != AuthLoginMode.TelegramLogin || !AuthenticationPolicy.TelegramAvailable(settings)))
            {
                c.UserId = 0; c.LoginMode = AuthLoginMode.TelegramLogin; c.Factor = OtpTypeId.Telegram;
                c.UseRecoveryCode = input.UseRecoveryCode;
                db.AuthenticationChallenges.Add(c);
                return await Describe(c, reference, ct);
            }
            if (input.LoginMode != AuthenticationPolicy.Mode(settings)) throw Unavailable("Choose the sign-in mode configured for this account");
            await PrepareSignIn(c, settings, input.Factor, input.UseRecoveryCode, ct);
            db.AuthenticationChallenges.Add(c);
            return await Describe(c, reference, ct);
        }, ct);
    }

    private async Task PrepareSignIn(AuthenticationChallenge c, AuthUserProperty? settings, OtpTypeId requestedFactor, bool recovery, CancellationToken ct)
    {
        c.UseRecoveryCode = recovery;
        if (c.LoginMode == AuthLoginMode.Password)
        {
            c.State = AuthChallengeState.Approved;
            return;
        }
        c.Factor = c.LoginMode == AuthLoginMode.TelegramLogin ? OtpTypeId.Telegram :
            requestedFactor == OtpTypeId.Unknown ? AuthenticationPolicy.PreferredFactor(settings) : requestedFactor;
        if (!recovery && c.LoginMode == AuthLoginMode.PasswordSecondFactor && !AuthenticationPolicy.FactorEnabled(settings, c.Factor))
            throw Unavailable("This second factor is not enabled");
        c.TelegramId = settings?.TelegramId;
        if (c.Factor == OtpTypeId.Email && !recovery)
            c.Email = (await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = c.UserId }, cancellationToken: ct)).Contact?.Email;
        await Deliver(c, ct);
    }

    public async Task<CompleteAuthChallengeResponse> Complete(CompleteAuthChallengeRequest input, CancellationToken ct)
    {
        await GuardRequest(input.Challenge?.Id ?? "", false, ct);
        return await WithChallenge(input.Challenge!, async c =>
        {
            if (c.State is not (AuthChallengeState.Waiting or AuthChallengeState.Approved or AuthChallengeState.Completed))
                return new CompleteAuthChallengeResponse { State = c.State };
            if (c.UserId != 0) await guard.EnsureUserAllowedAsync(c.UserId, ct);
            if (c.State == AuthChallengeState.Waiting && !await VerifyChallengeCode(c, input.Code, input.UseRecoveryCode, ct))
                return new CompleteAuthChallengeResponse { State = c.State, ErrorCode = "invalid_code" };
            if (c.Purpose == AuthenticationPurpose.SignIn && c.UserId == 0) throw Denied();

            var response = new CompleteAuthChallengeResponse { State = AuthChallengeState.Completed };
            if (c.Purpose == AuthenticationPurpose.Registration && c.State != AuthChallengeState.Completed)
            {
                if (c.Factor == OtpTypeId.Telegram && (!telegram.Configured || !c.TelegramId.HasValue)) throw Denied();
                if (c.TelegramId.HasValue && await db.AuthUserProperties.AnyAsync(x => x.TelegramId == c.TelegramId, ct))
                    throw Unavailable("Telegram is already linked to an account on this node");
                var draft = await users.AddDraftUserAsync(new AddDraftUserRequest
                {
                    Username = c.Username, FirstName = c.FirstName, LastName = c.LastName,
                    Email = c.Email ?? "", RegistrationId = c.Id.ToString()
                }, cancellationToken: ct);
                c.UserId = draft.UserId;
                var settings = await EnsureSettings(c.UserId, ct);
                settings.LoginMode = c.LoginMode;
                settings.PreferredFactor = (OtpType)c.Factor;
                settings.EmailOtpEnabled = c.Factor == OtpTypeId.Email && c.LoginMode == AuthLoginMode.PasswordSecondFactor;
                if (c.Factor == OtpTypeId.Telegram)
                {
                    settings.TelegramId = c.TelegramId; settings.TelegramUsername = c.TelegramUsername;
                    settings.TelegramEnabled = true; settings.TelegramOtpEnabled = true; settings.FastAuthTelegramEnabled = true;
                }
                await passwords.UpdateUserPasswordHash(c.UserId, c.PasswordHash!);
                await users.ConfirmUserAsync(new ConfirmUserRequest { UserId = c.UserId }, cancellationToken: ct);
                if (c.LoginMode != AuthLoginMode.Password) response.RecoveryCodes.AddRange(await ReplaceRecoveryCodes(c.UserId, ct));
                c.PasswordHash = null;
            }
            if (c.Purpose is AuthenticationPurpose.SignIn or AuthenticationPurpose.Registration)
            {
                response.Session = await Session(c, ct);
                await guard.ClearLoginFailuresAsync(c.Username, request.TrustedIpAddress, c.UserId, ct);
            }
            else if (c.Purpose == AuthenticationPurpose.TelegramBinding && c.State != AuthChallengeState.Completed)
            {
                if (AuthenticatedUser() != c.UserId || !telegram.Configured || !c.TelegramId.HasValue) throw Denied();
                if (await db.AuthUserProperties.AnyAsync(x => x.TelegramId == c.TelegramId && x.UserId != c.UserId, ct))
                    throw Unavailable("Telegram is already linked to an account on this node");
                var settings = await EnsureSettings(c.UserId, ct);
                settings.TelegramId = c.TelegramId; settings.TelegramUsername = c.TelegramUsername;
                settings.TelegramEnabled = true; settings.TelegramOtpEnabled = true; settings.FastAuthTelegramEnabled = true;
                await InvalidatePending(settings, c.Id, ct);
                c.PolicyVersion = settings.PolicyVersion;
                if (!await db.RecoveryCodes.AnyAsync(x => x.UserId == c.UserId && x.UsedAt == null, ct))
                    response.RecoveryCodes.AddRange(await ReplaceRecoveryCodes(c.UserId, ct));
            }
            else if (c.Purpose == AuthenticationPurpose.EmailBinding && c.State != AuthChallengeState.Completed)
            {
                if (AuthenticatedUser() != c.UserId) throw Denied();
                await users.SetVerifiedEmailAsync(new SetVerifiedEmailRequest { UserId = c.UserId, Email = c.Email! }, cancellationToken: ct);
                var settings = await EnsureSettings(c.UserId, ct);
                await InvalidatePending(settings, c.Id, ct);
                c.PolicyVersion = settings.PolicyVersion;
            }
            else if (c.Purpose is AuthenticationPurpose.Reauthentication or AuthenticationPurpose.PasswordRecovery)
            {
                if (c.ProofConsumed) throw Denied();
                if (c.Purpose == AuthenticationPurpose.Reauthentication && AuthenticatedUser() != c.UserId) throw Denied();
                response.SecurityProof = input.Challenge;
            }
            else if (c.Purpose == AuthenticationPurpose.FastAuth) throw Denied();
            c.State = AuthChallengeState.Completed;
            c.CodeHash = null; c.TelegramTokenHash = null;
            return response;
        }, ct);
    }

    private async Task<bool> VerifyChallengeCode(AuthenticationChallenge c, string code, bool useRecoveryCode, CancellationToken ct)
    {
        var valid = false;
        if ((useRecoveryCode || c.UseRecoveryCode) && c.UserId != 0 &&
            c.Purpose is AuthenticationPurpose.SignIn or AuthenticationPurpose.Reauthentication)
        {
            var hash = secrets.Hash("recovery:" + code.Trim().Replace("-", "").ToLowerInvariant());
            var recovery = await db.RecoveryCodes.SingleOrDefaultAsync(x => x.UserId == c.UserId && x.Hash == hash && x.UsedAt == null, ct);
            if (recovery != null) { recovery.UsedAt = Now; valid = true; }
        }
        else if (c.Factor == OtpTypeId.Authenticator && c.UserId != 0)
        {
            var settings = await Settings(c.UserId, ct);
            valid = settings is { OtpEnabled: true, OtpSecret: not null } && new Totp(Base32Encoding.ToBytes(settings.OtpSecret))
                .VerifyTotp(Now, code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
        }
        else if (!string.IsNullOrWhiteSpace(code) && c.CodeHash != null)
            valid = secrets.Matches(c.CodeHash, $"{c.Id}:{code.Trim()}");
        if (!valid)
        {
            c.FailedAttempts++;
            if (c.FailedAttempts >= 5) c.State = AuthChallengeState.Rejected;
            if (c.UserId != 0)
                await guard.RegisterLoginFailureAsync(c.Username, request.TrustedIpAddress, c.UserId, ct);
            return false;
        }
        c.State = AuthChallengeState.Approved;
        return true;
    }

    private async Task<AuthResponse> Session(AuthenticationChallenge c, CancellationToken ct)
    {
        RefreshToken? token;
        if (c.SessionId.HasValue)
            token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Id == c.SessionId && x.ExpiresAt > Now, ct) ?? throw Denied();
        else
        {
            var account = await users.GetByIdAsync(new GetByIdRequest { UserId = c.UserId }, cancellationToken: ct);
            if (account.User == null || account.User.IsBot) throw Denied();
            var old = await db.RefreshTokens.Where(x => x.UserId == c.UserId && x.DeviceId == c.DeviceId).ToListAsync(ct);
            db.RefreshTokens.RemoveRange(old);
            token = new RefreshToken { Value = RefreshTokenGenerator.GenerateRefreshToken(), UserId = c.UserId,
                DeviceId = c.DeviceId, CreatedAt = Now, ExpiresAt = Now.AddDays(9999) };
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
            c.SessionId = token.Id;
            await users.RegisterDeviceAsync(new RegisterDeviceRequest { UserId = c.UserId, DeviceId = c.DeviceId,
                OriginalName = c.DeviceName, OperationSystem = c.OperationSystem, AppName = c.AppName, Location = "" }, cancellationToken: ct);
        }
        if (c.ExpiresAt <= Now) throw Denied();
        return new AuthResponse { AccessToken = jwt.GenerateUserToken(c.UserId, c.DeviceId),
            RefreshToken = new Token { Value = token.Value, ExpirationDate = Timestamp.FromDateTime(token.ExpiresAt) } };
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(password) > 72)
            throw Invalid("Password must contain at least 8 characters and at most 72 UTF-8 bytes");
    }

    private static bool ValidEmail(string email) => email.Length <= 254 &&
        System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address == email;
}
