using BarkFluff.Proto.Identity;

namespace BarkFluff.Client.Core.Models;

public sealed record SignInChallenge(
    AuthChallengeReference Reference,
    AuthChallengeState State,
    OtpTypeId Factor,
    IReadOnlyList<OtpTypeId> AvailableFactors,
    bool NeedsCode,
    string TelegramUrl,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode);
