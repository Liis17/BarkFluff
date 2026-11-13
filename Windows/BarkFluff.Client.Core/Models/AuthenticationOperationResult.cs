namespace BarkFluff.Client.Core.Models;

public sealed record AuthenticationOperationResult(
    bool IsSuccess,
    string? ErrorResourceKey,
    BarkFluff.Proto.Identity.IdentityAccountRestrictionInfo? AccountRestriction = null)
{
    public static AuthenticationOperationResult Success() => new(true, null);

    public static AuthenticationOperationResult Failure(
        string errorResourceKey,
        BarkFluff.Proto.Identity.IdentityAccountRestrictionInfo? accountRestriction = null) =>
        new(false, errorResourceKey, accountRestriction);
}
