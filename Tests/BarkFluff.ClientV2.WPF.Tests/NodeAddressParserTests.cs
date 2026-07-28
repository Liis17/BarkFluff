using BarkFluff.ClientV2.WPF.Services;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class NodeAddressParserTests
{
    private readonly NodeAddressParser _parser = new();

    [Theory]
    [InlineData("node.example.com", "https://node.example.com")]
    [InlineData("http://node.example.com:7002", "http://node.example.com:7002")]
    [InlineData("192.168.1.10:7002", "https://192.168.1.10:7002")]
    public void Parse_ValidAddress_ReturnsNormalizedUri(string address, string expected)
    {
        var result = _parser.Parse(address);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Address!.GetLeftPart(UriPartial.Authority));
    }

    [Theory]
    [InlineData(null, NodeAddressError.Required)]
    [InlineData("", NodeAddressError.Required)]
    [InlineData("ftp://node.example.com", NodeAddressError.Invalid)]
    [InlineData("https://192.168.1.10", NodeAddressError.IpPortRequired)]
    [InlineData("https://node.example.com/path", NodeAddressError.Invalid)]
    public void Parse_InvalidAddress_ReturnsExpectedError(string? address, NodeAddressError expected)
    {
        var result = _parser.Parse(address);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error);
    }
}
