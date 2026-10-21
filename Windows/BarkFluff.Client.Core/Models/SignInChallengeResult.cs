namespace BarkFluff.Client.Core.Models;

public sealed record SignInChallengeResult(
    bool IsSuccess,
    SignInChallenge? Challenge,
    string? ErrorCode,
    string? ErrorResourceKey)
{
    public static SignInChallengeResult Success(SignInChallenge challenge) => new(true, challenge, null, null);

    public static SignInChallengeResult Failure(string? errorCode, string errorResourceKey) =>
        new(false, null, errorCode, errorResourceKey);
}
