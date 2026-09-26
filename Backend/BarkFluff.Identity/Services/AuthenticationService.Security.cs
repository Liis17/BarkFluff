using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService
{
    public async Task ValidateFactorRemoval(OtpTypeId factor, CancellationToken ct)
    {
        var settings = await Settings(AuthenticatedUser(), ct);
        if (AuthenticationPolicy.Mode(settings) == AuthLoginMode.PasswordSecondFactor &&
            !AvailableFactors(settings).Any(f => f != factor))
            throw Invalid("Select a different sign-in mode before removing the last factor");
    }

    public async Task<T> SetupFactor<T>(AuthChallengeReference reference, OtpTypeId factor, Func<Task<T>> action, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        await GuardRequest($"user:{userId}", true, ct);
        return await store.ForUser(userId, async () =>
        {
            var settings = await Settings(userId, ct);
            if (factor is not (OtpTypeId.Authenticator or OtpTypeId.Email) || AuthenticationPolicy.FactorEnabled(settings, factor))
                throw Invalid("Choose a factor that is not already enabled");
            if (factor == OtpTypeId.Email)
            {
                var contact = await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = userId }, cancellationToken: ct);
                if (!EmailAvailable || string.IsNullOrWhiteSpace(contact.Contact?.Email)) throw Unavailable("Verify an email first");
            }
            await ConsumeProof(reference, userId, ct);
            var proof = await Load(reference, ct);
            proof.ProofScope = "otp_setup:" + factor;
            return await action();
        }, ct);
    }

    public async Task<ConfirmOtpVerificationResponse> ConfirmFactorSetup(AuthChallengeReference reference, Func<Task<ConfirmOtpVerificationResponse>> action, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        return await store.ForUser(userId, async () =>
        {
            var proof = await Load(reference, ct);
            await RefreshState(proof, ct);
            var settings = await Settings(userId, ct) ?? throw Denied();
            if (proof.UserId != userId || proof.Purpose != AuthenticationPurpose.Reauthentication ||
                proof.State != AuthChallengeState.Completed || !proof.ProofConsumed ||
                proof.ProofScope != "otp_setup:" + (OtpTypeId)settings.SelectedOtpType) throw Denied();
            var result = await action();
            proof.ProofScope = "otp_setup_completed";
            await InvalidatePending(settings, proof.Id, ct);
            if (!await db.RecoveryCodes.AnyAsync(x => x.UserId == userId, ct))
                result.RecoveryCodes.AddRange(await ReplaceRecoveryCodes(userId, ct));
            return result;
        }, ct);
    }

    public async Task<T> RemoveFactor<T>(AuthChallengeReference reference, OtpTypeId factor, Func<Task<T>> action, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        return await store.ForUser(userId, async () =>
        {
            await ValidateFactorRemoval(factor, ct);
            await ConsumeProof(reference, userId, ct);
            var result = await action();
            var settings = await EnsureSettings(userId, ct);
            await InvalidatePending(settings, Guid.Empty, ct);
            return result;
        }, ct);
    }
    public Task<DisableOtpVerificationResponse> DisableFactor(DisableOtpVerificationRequest input, CancellationToken ct) =>
        RemoveFactor(input.SecurityProof, input.OtpType, async () =>
        {
            var settings = await EnsureSettings(AuthenticatedUser(), ct);
            if (input.OtpType == OtpTypeId.Authenticator) { settings.OtpEnabled = false; settings.OtpSecret = null; }
            else if (input.OtpType == OtpTypeId.Email) { settings.EmailOtpEnabled = false; settings.LastEmailAuthCode = null; }
            else throw Invalid("Use Telegram settings to disable Telegram");
            settings.PreferredFactor = (OtpType)PreferredAvailableFactor(settings);
            return new DisableOtpVerificationResponse();
        }, ct);

    public async Task<SecuritySettingsResponse> GetSecuritySettings(CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        var settings = await Settings(userId, ct);
        var contacts = await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = userId }, cancellationToken: ct);
        return new SecuritySettingsResponse
        {
            LoginMode = AuthenticationPolicy.Mode(settings), PreferredFactor = PreferredAvailableFactor(settings),
            AuthenticatorEnabled = settings?.OtpEnabled == true, EmailEnabled = settings?.EmailOtpEnabled == true,
            TelegramLinked = settings?.TelegramId.HasValue == true, TelegramEnabled = settings?.TelegramEnabled == true,
            TelegramOtpEnabled = settings?.TelegramOtpEnabled == true, FastAuthTelegramEnabled = settings?.FastAuthTelegramEnabled == true,
            TelegramUsername = settings?.TelegramUsername ?? "", VerifiedEmail = contacts.Contact?.Email ?? "",
            RemainingRecoveryCodes = await db.RecoveryCodes.CountAsync(x => x.UserId == userId && x.UsedAt == null, ct)
        };
    }

    public async Task<AuthChallengeResponse> BeginReauthentication(BeginReauthenticationRequest input, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        await GuardRequest($"user:{userId}", true, ct);
        return await store.ForUser(userId, async () =>
        {
            var settings = await Settings(userId, ct);
            var account = await users.GetByIdAsync(new GetByIdRequest { UserId = userId }, cancellationToken: ct);
            if (AuthenticationPolicy.Mode(settings) != AuthLoginMode.TelegramLogin)
                await VerifyPassword(userId, account.User.Username, input.Password, ct);
            var (c, reference) = NewChallenge(AuthenticationPurpose.Reauthentication, userId, account.User.Username, settings);
            await PrepareSignIn(c, settings, input.Factor, input.UseRecoveryCode, ct);
            db.AuthenticationChallenges.Add(c);
            return await Describe(c, reference, ct);
        }, ct);
    }

    private async Task ConsumeProof(AuthChallengeReference reference, long userId, CancellationToken ct)
    {
        var proof = await Load(reference, ct);
        await RefreshState(proof, ct);
        if (proof.Purpose != AuthenticationPurpose.Reauthentication || proof.UserId != userId ||
            proof.State != AuthChallengeState.Completed || proof.ProofConsumed) throw Denied();
        proof.ProofConsumed = true;
    }

    private async Task InvalidatePending(AuthUserProperty settings, Guid except, CancellationToken ct)
    {
        settings.PolicyVersion++;
        var pending = await db.AuthenticationChallenges.Where(x => x.UserId == settings.UserId && x.Id != except &&
            (x.State == AuthChallengeState.Waiting || x.State == AuthChallengeState.Approved ||
                (x.State == AuthChallengeState.Completed && x.Purpose == AuthenticationPurpose.Reauthentication))).ToListAsync(ct);
        foreach (var challenge in pending) challenge.State = AuthChallengeState.Cancelled;
    }

    public async Task<SecuritySettingsResponse> UpdateSecuritySettings(UpdateSecuritySettingsRequest input, bool unlink, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        await GuardRequest($"user:{userId}", false, ct);
        var recoveryCodes = await store.ForUser(userId, async () =>
        {
            var settings = await EnsureSettings(userId, ct);
            var hasTelegram = !unlink && settings.TelegramId.HasValue;
            var enabled = hasTelegram && input.TelegramEnabled;
            if (input.TelegramEnabled && (!hasTelegram || !telegram.Configured)) throw Unavailable("Link Telegram first");
            if (input.LoginMode == AuthLoginMode.TelegramLogin && !enabled) throw Invalid("Choose a mode that does not require Telegram");
            if (input.LoginMode is not (AuthLoginMode.Password or AuthLoginMode.TelegramLogin or AuthLoginMode.PasswordSecondFactor))
                throw Invalid("Invalid sign-in mode");
            var factorAvailable = input.PreferredFactor switch
            {
                OtpTypeId.Authenticator => settings.OtpEnabled,
                OtpTypeId.Email => settings.EmailOtpEnabled && EmailAvailable,
                OtpTypeId.Telegram => enabled && input.TelegramOtpEnabled,
                _ => false
            };
            if (input.LoginMode == AuthLoginMode.PasswordSecondFactor && !factorAvailable)
                throw Invalid("Enable a second factor before selecting this mode");
            if (input.LoginMode != AuthLoginMode.TelegramLogin && string.IsNullOrWhiteSpace(await passwords.GetUserPasswordHash(userId)))
                throw Invalid("Set a password first");
            await ConsumeProof(input.SecurityProof, userId, ct);
            settings.LoginMode = input.LoginMode; settings.PreferredFactor = (OtpType)input.PreferredFactor;
            settings.TelegramEnabled = enabled; settings.TelegramOtpEnabled = enabled && input.TelegramOtpEnabled;
            settings.FastAuthTelegramEnabled = enabled && input.FastAuthTelegramEnabled;
            if (unlink) { settings.TelegramId = null; settings.TelegramUsername = null; }
            await InvalidatePending(settings, Guid.Empty, ct);
            return input.LoginMode != AuthLoginMode.Password && !await db.RecoveryCodes.AnyAsync(x => x.UserId == userId, ct)
                ? await ReplaceRecoveryCodes(userId, ct) : Array.Empty<string>();
        }, ct);
        var response = await GetSecuritySettings(ct);
        response.RecoveryCodes.AddRange(recoveryCodes);
        return response;
    }

    public async Task<AuthChallengeResponse> BeginTelegramBinding(SecurityProofRequest input, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        if (!telegram.Configured) throw Unavailable("Telegram is unavailable");
        await GuardRequest($"user:{userId}", true, ct);
        return await store.ForUser(userId, async () =>
        {
            var settings = await Settings(userId, ct);
            if (settings?.TelegramId.HasValue == true) throw Unavailable("Unlink the previous Telegram account first");
            await ConsumeProof(input.SecurityProof, userId, ct);
            var account = await users.GetByIdAsync(new GetByIdRequest { UserId = userId }, cancellationToken: ct);
            var (c, reference) = NewChallenge(AuthenticationPurpose.TelegramBinding, userId, account.User.Username, settings);
            c.Factor = OtpTypeId.Telegram;
            var url = await Deliver(c, ct);
            db.AuthenticationChallenges.Add(c);
            var response = await Describe(c, reference, ct); response.TelegramUrl = url;
            return response;
        }, ct);
    }

    public async Task<AuthChallengeResponse> BeginEmailBinding(BeginEmailBindingRequest input, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        if (!EmailAvailable || !ValidEmail(input.Email.Trim())) throw Invalid("A valid email and enabled email delivery are required");
        await GuardRequest($"user:{userId}", true, ct);
        return await store.ForUser(userId, async () =>
        {
            await ConsumeProof(input.SecurityProof, userId, ct);
            var account = await users.GetByIdAsync(new GetByIdRequest { UserId = userId }, cancellationToken: ct);
            var (c, reference) = NewChallenge(AuthenticationPurpose.EmailBinding, userId, account.User.Username, await Settings(userId, ct));
            c.Email = input.Email.Trim(); c.Factor = OtpTypeId.Email;
            await Deliver(c, ct);
            db.AuthenticationChallenges.Add(c);
            return await Describe(c, reference, ct);
        }, ct);
    }

    private async Task<string[]> ReplaceRecoveryCodes(long userId, CancellationToken ct)
    {
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(x => x.UserId == userId).ToListAsync(ct));
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()).ToArray();
        db.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode { Id = Guid.NewGuid(), UserId = userId, Hash = secrets.Hash("recovery:" + code) }));
        return codes;
    }

    public async Task<RecoveryCodesResponse> GenerateRecoveryCodes(SecurityProofRequest input, CancellationToken ct)
    {
        var userId = AuthenticatedUser();
        await GuardRequest($"user:{userId}", false, ct);
        return await store.ForUser(userId, async () =>
        {
            await ConsumeProof(input.SecurityProof, userId, ct);
            var result = new RecoveryCodesResponse();
            result.Codes.AddRange(await ReplaceRecoveryCodes(userId, ct));
            return result;
        }, ct);
    }

    public async Task<AuthChallengeResponse> BeginPasswordRecovery(BeginPasswordRecoveryRequest input, CancellationToken ct)
    {
        DeviceId();
        if (!EmailAvailable) throw Unavailable("Email recovery is unavailable; use an existing session or a recovery code to sign in");
        await GuardRequest(input.Login, true, ct);
        var account = await FindUser(input.Login, ct);
        var settings = account == null ? null : await Settings(account.Id, ct);
        var (c, reference) = NewChallenge(AuthenticationPurpose.PasswordRecovery, account?.Id ?? 0, account?.Username ?? input.Login, settings);
        c.Factor = OtpTypeId.Email;
        if (account != null)
        {
            c.Email = (await users.GetUserContactsAsync(new GetUserContactsRequest { UserId = account.Id }, cancellationToken: ct)).Contact?.Email;
            if (!string.IsNullOrWhiteSpace(c.Email)) await Deliver(c, ct);
        }
        // Even a missing account has a code-entry screen, but can never yield a proof.
        c.CodeHash ??= secrets.Hash(AuthenticationSecrets.NewSecret());
        db.AuthenticationChallenges.Add(c); await db.SaveChangesAsync(ct);
        return await Describe(c, reference, ct);
    }

    public async Task<SetPasswordResponse> SetRecoveredPassword(SetRecoveredPasswordRequest input, CancellationToken ct)
    {
        ValidatePassword(input.Password);
        await GuardRequest(input.SecurityProof?.Id ?? "", false, ct);
        return await WithChallenge(input.SecurityProof!, async c =>
        {
            if (c.Purpose != AuthenticationPurpose.PasswordRecovery || c.State != AuthChallengeState.Completed || c.ProofConsumed || c.UserId == 0)
                throw Denied();
            await passwords.UpdateUserPasswordHash(c.UserId, PasswordHasher.HashPassword(input.Password));
            c.ProofConsumed = true;
            await InvalidatePending(await EnsureSettings(c.UserId, ct), c.Id, ct);
            return new SetPasswordResponse();
        }, ct);
    }
}
