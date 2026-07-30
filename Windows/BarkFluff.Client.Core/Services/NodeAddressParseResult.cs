namespace BarkFluff.Client.Core.Services;

public enum NodeAddressError
{
    None,
    Required,
    Invalid,
    IpPortRequired
}

public sealed record NodeAddressParseResult(Uri? Address, NodeAddressError Error)
{
    public bool IsSuccess => Address is not null;

    public static NodeAddressParseResult Success(Uri address) => new(address, NodeAddressError.None);

    public static NodeAddressParseResult Failure(NodeAddressError error) => new(null, error);
}
