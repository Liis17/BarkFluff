namespace BarkFluff.Identity.Settings;

public class IdentitySecurityOptions
{
    public int HighRiskRequestsPerMinute { get; set; } = 60;

    public int SubjectRequestsPerWindow { get; set; } = 5;

    public int SubjectWindowMinutes { get; set; } = 15;

    public int FailureLimit { get; set; } = 5;

    public int FailureWindowMinutes { get; set; } = 15;

    public int LockoutMinutes { get; set; } = 15;

    public int CodeAttemptLimit { get; set; } = 5;

    public int OtpAttemptLimit { get; set; } = 5;

    public int BackoffBaseMilliseconds { get; set; } = 250;

    public int BackoffMaxMilliseconds { get; set; } = 2000;
}
