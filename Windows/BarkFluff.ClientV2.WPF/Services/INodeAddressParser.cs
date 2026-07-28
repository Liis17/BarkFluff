namespace BarkFluff.ClientV2.WPF.Services;

public interface INodeAddressParser
{
    NodeAddressParseResult Parse(string? address);
}
