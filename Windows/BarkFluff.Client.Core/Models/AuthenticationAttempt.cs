namespace BarkFluff.Client.Core.Models;

/// <summary>An opaque handle for one node-bound authentication flow.</summary>
public sealed record AuthenticationAttempt(Guid Id)
{
    public static AuthenticationAttempt Create() => new(Guid.NewGuid());
}
