using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Shared.Identity;

using Xunit;

namespace BarkFluff.Settings.Tests.Catalog;

public sealed class SettingsCatalogTests
{
    [Fact]
    public void Catalog_is_unique_and_covers_every_scope()
    {
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All
            .Select(entry => (entry.ServiceId, entry.StorageKey))
            .Distinct()
            .Count());

        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All
            .Select(entry => (entry.ServiceId, entry.Section, entry.Key))
            .Distinct()
            .Count());

        Assert.Equal(
            SettingsScopes.All.Select(scope => scope.ServiceId).Order(),
            SettingsCatalog.All.Select(entry => entry.ServiceId).Distinct().Order());
    }

    [Theory]
    [InlineData((int)ServiceId.Identity, "IdentityDb", "", "IdentityDb")]
    [InlineData((int)ServiceId.Beacon, "NavigatorUrl", "", "NavigatorUrl")]
    [InlineData((int)ServiceId.Files, "S3Buckets:message-audio", "SecretKey", "S3Buckets:message-audio:SecretKey")]
    [InlineData((int)ServiceId.Identity, "IdentitySecurity", "FailureLimit", "IdentitySecurity:FailureLimit")]
    public void Legacy_key_mapping_is_reversible(int serviceId, string section, string key, string storageKey)
    {
        var entry = SettingsCatalog.Resolve((ServiceId)serviceId, section, key);

        Assert.Equal(storageKey, entry.StorageKey);
        Assert.Same(entry, SettingsCatalog.Resolve((ServiceId)serviceId, storageKey));
    }

    [Fact]
    public void Unknown_key_is_rejected()
    {
        Assert.Throws<UnknownSettingException>(() =>
            SettingsCatalog.Resolve(ServiceId.Users, "Anything", "Goes"));
    }
}
