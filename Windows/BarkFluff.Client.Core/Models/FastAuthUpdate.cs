namespace BarkFluff.Client.Core.Models;

public enum FastAuthUpdateKind
{
    Scanned,
    Accepted,
    Rejected,
    Expired,
    Failed
}

public sealed record FastAuthUpdate(FastAuthUpdateKind Kind, string? ErrorResourceKey = null);
