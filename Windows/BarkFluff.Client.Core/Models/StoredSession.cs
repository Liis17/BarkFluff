namespace BarkFluff.Client.Core.Models;

public sealed record StoredSession(string AccessToken, string RefreshToken)
{
    public string? Node { get; init; }
    public long? UserId { get; init; }
    public string? UserName { get; init; }
}
