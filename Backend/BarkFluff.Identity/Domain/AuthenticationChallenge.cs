using BarkFluff.Proto.Identity;

namespace BarkFluff.Identity.Domain;

public enum AuthenticationPurpose { Registration, SignIn, Reauthentication, TelegramBinding, EmailBinding, PasswordRecovery, FastAuth }

public class AuthenticationChallenge
{
    public Guid Id { get; set; }
    public AuthenticationPurpose Purpose { get; set; }
    public AuthChallengeState State { get; set; } = AuthChallengeState.Waiting;
    public long UserId { get; set; }
    public string SecretHash { get; set; } = "";
    public string? TelegramTokenHash { get; set; }
    public string? CodeHash { get; set; }
    public OtpTypeId Factor { get; set; }
    public bool UseRecoveryCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int FailedAttempts { get; set; }
    public int PolicyVersion { get; set; }
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string OperationSystem { get; set; } = "";
    public string AppName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Username { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public AuthLoginMode LoginMode { get; set; }
    public long? TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? AttemptId { get; set; }
    public long? SessionId { get; set; }
    public bool ProofConsumed { get; set; }
    public string ProofScope { get; set; } = "";
}

public class RecoveryCode
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string Hash { get; set; } = "";
    public DateTime? UsedAt { get; set; }
}

public class TelegramPollingState
{
    public long Id { get; set; }
    public long Offset { get; set; }
}
