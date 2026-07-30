namespace BarkFluff.Client.Core.Services;

public interface INodeAddressParser
{
    NodeAddressParseResult Parse(string? address);
}
