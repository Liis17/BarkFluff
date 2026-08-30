using Barkfluff.AdminPanel.Endpoints;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Endpoints;

public class ConfigurationEndpointsTests
{
    [Theory]
    [InlineData("DevelopersDb", "")]
    [InlineData("Redis", "")]
    [InlineData("NavigatorUrl", "")]
    [InlineData("RunSettings", "Port")]
    public void ValidateConfigurationUpdateRequest_AllowsSectionOnlyAndNamedKeys(string section, string key)
    {
        var request = new ConfigurationValueUpdateRequest
        {
            Section = section,
            Key = key
        };

        Assert.Null(ConfigurationEndpoints.ValidateConfigurationUpdateRequest(request));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "Port")]
    public void ValidateConfigurationUpdateRequest_RejectsMissingSection(string section, string key)
    {
        var request = new ConfigurationValueUpdateRequest
        {
            Section = section,
            Key = key
        };

        Assert.Equal(
            "Section обязательна",
            ConfigurationEndpoints.ValidateConfigurationUpdateRequest(request));
    }

    [Theory]
    [InlineData("DevelopersDb", " ")]
    [InlineData("DevelopersDb", "\t")]
    public void ValidateConfigurationUpdateRequest_RejectsWhitespaceOnlyKey(string section, string key)
    {
        var request = new ConfigurationValueUpdateRequest
        {
            Section = section,
            Key = key
        };

        Assert.Equal(
            "Key должен быть пустым или содержать непробельные символы",
            ConfigurationEndpoints.ValidateConfigurationUpdateRequest(request));
    }

    [Theory]
    [InlineData("DevelopersDb", " ")]
    [InlineData("Redis", "\t")]
    [InlineData("NavigatorUrl", "\r\n")]
    public void NormalizeConfigurationKey_CanonicalizesWhitespaceOnlySectionKeys(string section, string key)
    {
        Assert.Equal("", ConfigurationEndpoints.NormalizeConfigurationKey(section, key));
    }
}
