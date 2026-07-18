using BarkFluff.Navigator.Features.RegisterServer;

namespace BarkFluff.Navigator.Tests.Features;

public class FederationServernameGuardTests
{
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("localhost")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_RejectsInvalid(string input)
    {
        FederationServernameGuard.TryNormalize(input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_AcceptsValidHostname()
    {
        FederationServernameGuard.TryNormalize("Chat.Example.ORG", out var normalized).Should().BeTrue();
        normalized.Should().Be("chat.example.org");
    }

    [Fact]
    public void TryNormalize_PunycodeHomograph_NormalizesToALabel()
    {
        FederationServernameGuard.TryNormalize("exаmple.org", out var normalized).Should().BeTrue();
        normalized.Should().StartWith("xn--");
    }
}
