using System.Security.Cryptography;
using BarkFluff.Status.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarkFluff.Status.Tests;

public class StatusOptionsTests
{
    private static StatusOptions Load(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();
        return StatusOptions.From(configuration);
    }

    private static readonly (string, string?) Domain = ("NODE_DOMAIN", "node.example.test");

    [Fact]
    public void MissingNodeDomainIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Load());
    }

    [Theory]
    [InlineData("https://node.example.test")]
    [InlineData("node.example.test:443")]
    [InlineData("localhost")]
    public void NodeDomainMustBeBareDnsName(string domain)
    {
        Assert.Throws<InvalidOperationException>(() => Load(("NODE_DOMAIN", domain)));
    }

    [Fact]
    public void DerivesThirteenPublicHostsFromNodeDomain()
    {
        var options = Load(Domain);

        Assert.Equal(13, options.ServiceOrigins.Count);
        Assert.Equal(new Uri("https://messages.node.example.test"), options.ServiceOrigins["messages"]);
        Assert.Equal(new Uri("https://files.node.example.test"), options.ServiceOrigins["files"]);
        Assert.Equal(new Uri("https://fast-auth.node.example.test"), options.ServiceOrigins["fast-auth"]);
        Assert.False(options.ServiceOrigins.ContainsKey("files-media"));
        Assert.False(options.ServiceOrigins.ContainsKey("admin-panel"));
        Assert.Null(options.FederationSpkiPin);
        Assert.Equal("data/status.db", options.DatabasePath);
    }

    [Fact]
    public void OperatorOverrideReplacesOneService()
    {
        var options = Load(Domain, ("Status:ServiceOrigins:messages", "https://msg.other.example.org:8443/"));

        Assert.Equal(new Uri("https://msg.other.example.org:8443"), options.ServiceOrigins["messages"]);
        Assert.Equal(new Uri("https://identity.node.example.test"), options.ServiceOrigins["identity"]);
    }

    [Fact]
    public void OperatorOverrideAcceptsHyphenatedServiceKey()
    {
        var options = Load(Domain, ("Status:ServiceOrigins:fast-auth", "https://auth.other.example.org"));

        Assert.Equal(new Uri("https://auth.other.example.org"), options.ServiceOrigins["fast-auth"]);
    }

    [Theory]
    [InlineData("http://msg.example.org")]
    [InlineData("https://user:pw@msg.example.org")]
    [InlineData("https://msg.example.org/path")]
    [InlineData("https://msg.example.org/?q=1")]
    [InlineData("not a url")]
    public void OverrideMustBeHttpsOriginOnly(string origin)
    {
        Assert.Throws<InvalidOperationException>(() => Load(Domain, ("Status:ServiceOrigins:messages", origin)));
    }

    [Fact]
    public void OverrideForUnknownServiceIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Load(Domain, ("Status:ServiceOrigins:setup", "https://setup.example.org")));
    }

    [Fact]
    public void DisabledServicesAreExcludedAndValidated()
    {
        var options = Load(Domain, ("Status:DisabledServices", "calls, web"));

        Assert.Equal(11, options.ServiceOrigins.Count);
        Assert.False(options.ServiceOrigins.ContainsKey("calls"));
        Assert.False(options.ServiceOrigins.ContainsKey("web"));
        Assert.Throws<InvalidOperationException>(() => Load(Domain, ("Status:DisabledServices", "nonexistent")));
    }

    [Fact]
    public void AllServicesDisabledIsRejected()
    {
        var all = string.Join(',', BarkFluff.Status.Catalog.PublicServices.All.Select(s => s.Key));
        Assert.Throws<InvalidOperationException>(() => Load(Domain, ("Status:DisabledServices", all)));
    }

    [Fact]
    public void ValidFederationPinIsNormalized()
    {
        var pin = Convert.ToBase64String(SHA256.HashData("key"u8.ToArray()));

        var options = Load(Domain, ("FEDERATION_SPKI_PIN", $" {pin} "));

        Assert.Equal(pin, options.FederationSpkiPin);
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void InvalidFederationPinIsRejectedWithoutEchoingIt(string pin)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(Domain, ("FEDERATION_SPKI_PIN", pin)));

        Assert.DoesNotContain(pin, error.Message);
    }

    [Fact]
    public void DatabasePathIsConfigurable()
    {
        var options = Load(Domain, ("Status:DatabasePath", "/var/lib/barkfluff-status/status.db"));

        Assert.Equal("/var/lib/barkfluff-status/status.db", options.DatabasePath);
    }
}
