using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Services;

public sealed partial class AuthenticationService
{
    public Task<CreateSessionForUserServerResponse> CreateFastAuthSession(CreateSessionForUserServerRequest input, CancellationToken ct)
    {
        if (input.UserId <= 0 || !Guid.TryParse(input.DeviceId, out var device) ||
            string.IsNullOrWhiteSpace(input.DeviceName) || string.IsNullOrWhiteSpace(input.OperationSystem) ||
            string.IsNullOrWhiteSpace(input.AppName)) throw Invalid("User and device metadata are required");
        return store.ForUser(input.UserId, async () =>
        {
            var settings = await Settings(input.UserId, ct);
            var needsTelegram = AuthenticationPolicy.TelegramAvailable(settings) && settings!.FastAuthTelegramEnabled;
            var legacy = string.IsNullOrEmpty(input.AttemptId);
            if (legacy && needsTelegram) throw Unavailable("Use the web FastAuth Telegram confirmation flow");
            if (!legacy && (!Guid.TryParse(input.AttemptId, out _) || input.ExpiresAt == null))
                throw Invalid("FastAuth attempt and expiry are required");
            var attempt = legacy ? "legacy:" + device : input.AttemptId;
            var c = await db.AuthenticationChallenges.SingleOrDefaultAsync(x => x.AttemptId == attempt, ct);
            if (c == null)
            {
                var expires = legacy ? Now.AddMinutes(5) : input.ExpiresAt.ToDateTime();
                if (expires <= Now) return new CreateSessionForUserServerResponse { ConfirmationState = AuthChallengeState.Expired };
                if (expires > Now.AddMinutes(5)) throw Invalid("FastAuth expiry exceeds five minutes");
                var account = await users.GetByIdAsync(new GetByIdRequest { UserId = input.UserId }, cancellationToken: ct);
                if (account.User == null || account.User.IsBot) throw Denied();
                c = new AuthenticationChallenge
                {
                    Id = Guid.NewGuid(), AttemptId = attempt, Purpose = AuthenticationPurpose.FastAuth,
                    UserId = input.UserId, Username = account.User.Username,
                    SecretHash = secrets.Hash(AuthenticationSecrets.NewSecret()), CreatedAt = Now, ExpiresAt = expires,
                    DeviceId = device.ToString(), DeviceName = input.DeviceName, OperationSystem = input.OperationSystem,
                    AppName = input.AppName, IpAddress = input.IpAddress, PolicyVersion = settings?.PolicyVersion ?? 0,
                    TelegramId = settings?.TelegramId, Factor = needsTelegram ? OtpTypeId.Telegram : OtpTypeId.Unknown,
                    State = needsTelegram ? AuthChallengeState.Waiting : AuthChallengeState.Approved
                };
                if (needsTelegram) await Deliver(c, ct);
                db.AuthenticationChallenges.Add(c);
            }
            if (c.Purpose != AuthenticationPurpose.FastAuth || c.UserId != input.UserId || c.DeviceId != device.ToString() ||
                (!legacy && c.ExpiresAt != input.ExpiresAt.ToDateTime())) throw Denied();
            await RefreshState(c, ct);
            var response = new CreateSessionForUserServerResponse { ConfirmationState = c.State };
            if (c.State is not (AuthChallengeState.Approved or AuthChallengeState.Completed)) return response;
            if (needsTelegram && c.Factor != OtpTypeId.Telegram) throw Denied();
            var session = await Session(c, ct);
            c.State = AuthChallengeState.Completed;
            c.TelegramTokenHash = null;
            response.ConfirmationState = c.State;
            response.AccessToken = session.AccessToken; response.RefreshToken = session.RefreshToken;
            return response;
        }, ct);
    }
}
