using BarkFluff.Proto.Identity;

namespace BarkFluff.Client.Core.Models;

public sealed record SignInResolutionResult(
    bool IsSuccess,
    AuthLoginMode LoginMode,
    string? ErrorCode,
    string? ErrorResourceKey)
{
    public static SignInResolutionResult Success(AuthLoginMode loginMode) => new(true, loginMode, null, null);

    public static SignInResolutionResult Failure(string? errorCode, string errorResourceKey) =>
        new(false, AuthLoginMode.LoginModeUnspecified, errorCode, errorResourceKey);
}
