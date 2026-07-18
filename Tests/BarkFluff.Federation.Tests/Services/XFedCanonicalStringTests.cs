using System.Security.Cryptography;
using System.Text;

using BarkFluff.Federation.Services;

using FluentAssertions;

namespace BarkFluff.Federation.Tests.Services;

public class XFedCanonicalStringTests
{
    [Fact]
    public void Build_ProducesExpectedFixedVector()
    {
        var requestBytes = "hello-federation"u8.ToArray();
        const string origin = "a.example";
        const string destination = "b.example";
        const long timestampMs = 1700000000000;
        const string method = "/barkfluff.federation.FederationS2SApi/Ping";

        var result = XFedCanonicalString.Build(origin, destination, timestampMs, method, requestBytes);

        var expectedHashHex = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant();
        var expected = $"{origin}\n{destination}\n{timestampMs}\n{method}\n{expectedHashHex}";

        Encoding.UTF8.GetString(result).Should().Be(expected);
    }

    [Fact]
    public void Build_DifferentRequestBytes_ProducesDifferentString()
    {
        var a = XFedCanonicalString.Build("a", "b", 1, "/m", "one"u8.ToArray());
        var b = XFedCanonicalString.Build("a", "b", 1, "/m", "two"u8.ToArray());

        a.Should().NotBeEquivalentTo(b);
    }
}
