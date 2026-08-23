using Barkfluff.AdminPanel.Services;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

public class ConfigurationFieldCatalogTests
{
    [Theory]
    [InlineData("JwtSettings", "SecretKey", "", ConfigurationFieldType.Secret)]
    [InlineData("Feature", "Enabled", "true", ConfigurationFieldType.Boolean)]
    [InlineData("Server", "Port", "7001", ConfigurationFieldType.Integer)]
    [InlineData("ExternalEndpoint", "Host", "https://example.org", ConfigurationFieldType.Url)]
    [InlineData("Limits", "MaxItems", "100", ConfigurationFieldType.Integer)]
    [InlineData("Profile", "DisplayName", "BarkFluff", ConfigurationFieldType.String)]
    public void Describe_InfersTypedField(string section, string key, string value, ConfigurationFieldType expected)
    {
        Assert.Equal(expected, ConfigurationFieldCatalog.Describe(section, key, value).Type);
    }

    [Fact]
    public void Describe_Port_AddsPortRange()
    {
        var field = ConfigurationFieldCatalog.Describe("Server", "Http1Port", "7001");

        Assert.Equal(1, field.Minimum);
        Assert.Equal(65535, field.Maximum);
    }

    [Theory]
    [InlineData("Feature", "Enabled", "yes")]
    [InlineData("Server", "Port", "70000")]
    [InlineData("ExternalEndpoint", "Url", "ftp://example.org")]
    [InlineData("JwtSettings", "SecretKey", "")]
    public void Validate_RejectsInvalidTypedValue(string section, string key, string value)
    {
        var field = ConfigurationFieldCatalog.Describe(section, key, value);

        Assert.NotNull(ConfigurationFieldCatalog.Validate(field, value));
    }

    [Theory]
    [InlineData("Feature", "Enabled", "false")]
    [InlineData("Server", "Port", "443")]
    [InlineData("ExternalEndpoint", "Host", "https://example.org/api")]
    [InlineData("JwtSettings", "SecretKey", "new-secret")]
    public void Validate_AcceptsValidTypedValue(string section, string key, string value)
    {
        var field = ConfigurationFieldCatalog.Describe(section, key, value);

        Assert.Null(ConfigurationFieldCatalog.Validate(field, value));
    }

    [Fact]
    public void Describe_BareInfrastructureHost_RemainsString()
    {
        var field = ConfigurationFieldCatalog.Describe("RabbitMQ", "Host", "rabbitmq");

        Assert.Equal(ConfigurationFieldType.String, field.Type);
    }
}
