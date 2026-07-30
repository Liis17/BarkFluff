namespace BarkFluff.Client.Core.Models;

public sealed record RegistrationStartResult(bool IsSuccess, string? CodeId, string? ErrorResourceKey)
{
    public static RegistrationStartResult Success(string codeId) => new(true, codeId, null);

    public static RegistrationStartResult Failure(string errorResourceKey) => new(false, null, errorResourceKey);
}
