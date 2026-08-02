namespace BarkFluff.Client.Core.Models;

public sealed record LoginResult(bool IsSuccess, bool RequiresTwoFactor, string? ErrorResourceKey)
{
    public static LoginResult Success() => new(true, false, null);

    public static LoginResult Failure(string errorResourceKey, bool requiresTwoFactor = false) =>
        new(false, requiresTwoFactor, errorResourceKey);
}
