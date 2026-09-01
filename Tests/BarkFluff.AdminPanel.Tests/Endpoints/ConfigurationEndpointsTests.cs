using Barkfluff.AdminPanel.Endpoints;
using BarkFluff.Shared.Identity;

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

    [Theory]
    [InlineData(ServiceId.Unknown, "GlobalSettings")]
    [InlineData(ServiceId.Identity, "IdentitySettings")]
    [InlineData(ServiceId.Notifications, "NotificationsSettings")]
    [InlineData(ServiceId.CloudMessaging, "CloudMessagingSettings")]
    [InlineData(ServiceId.Federation, "FederationSettings")]
    public void GetSettingsTableName_MapsCompatibilityServiceIdToCurrentTable(ServiceId serviceId, string expected)
    {
        Assert.Equal(expected, ConfigurationEndpoints.GetSettingsTableName(serviceId));
    }

    [Theory]
    [InlineData("Redis", "", "Redis")]
    [InlineData("RunSettings", "Port", "RunSettings:Port")]
    [InlineData("S3Buckets:message-audio", "SecretKey", "S3Buckets:message-audio:SecretKey")]
    public void GetStorageKey_ReconstructsCurrentSettingsKey(string section, string key, string expected)
    {
        Assert.Equal(expected, ConfigurationEndpoints.GetStorageKey(section, key));
    }
}
