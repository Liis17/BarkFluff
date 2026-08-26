namespace BarkFluff.Identity.Security;

public sealed record IdentityFailureResult(int Attempts, bool Locked, bool NewlyLocked = false);
