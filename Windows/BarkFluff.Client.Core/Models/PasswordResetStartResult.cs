namespace BarkFluff.Client.Core.Models;

public sealed record PasswordResetStartResult(bool IsSuccess, string? ResetId, string? ErrorResourceKey)
{
    public static PasswordResetStartResult Success(string resetId) => new(true, resetId, null);

    public static PasswordResetStartResult Failure(string errorResourceKey) => new(false, null, errorResourceKey);
}
