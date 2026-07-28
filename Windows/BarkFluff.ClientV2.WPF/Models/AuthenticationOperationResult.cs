namespace BarkFluff.ClientV2.WPF.Models;

public sealed record AuthenticationOperationResult(bool IsSuccess, string? ErrorResourceKey)
{
    public static AuthenticationOperationResult Success() => new(true, null);

    public static AuthenticationOperationResult Failure(string errorResourceKey) => new(false, errorResourceKey);
}
