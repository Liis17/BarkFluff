using BarkFluff.Federation.Services;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Federation.Tests.Services;

public class FederationSwitchTests
{
    private static FederationSwitch CreateSwitch(string? enabled, string? serverName)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = enabled,
            ["Federation:ServerName"] = serverName,
        }).Build());

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData(null, false)]
    public void IsEnabled_ParsesConfigCaseInsensitively(string? enabled, bool expected)
        => CreateSwitch(enabled, "node-a.test").IsEnabled.Should().Be(expected);

    [Theory]
    [InlineData("node-a.test", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsConfigured_RequiresNonBlankServerName(string? serverName, bool expected)
        => CreateSwitch("true", serverName).IsConfigured.Should().Be(expected);

    [Theory]
    [InlineData("true", "node-a.test", true)]
    [InlineData("true", null, false)]
    [InlineData("false", "node-a.test", false)]
    [InlineData("false", null, false)]
    public void IsActive_OnlyWhenEnabledAndConfigured(string? enabled, string? serverName, bool expected)
        => CreateSwitch(enabled, serverName).IsActive.Should().Be(expected);
}
