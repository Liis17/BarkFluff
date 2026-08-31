using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Shared.Identity;

using System.Security.Cryptography;
using System.Text;

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

        var expectedCounts = new Dictionary<ServiceId, int>
        {
            [ServiceId.Unknown] = 19, [ServiceId.Identity] = 14, [ServiceId.Users] = 7,
            [ServiceId.Beacon] = 10, [ServiceId.Notifications] = 5, [ServiceId.Files] = 44,
            [ServiceId.Messages] = 6, [ServiceId.FastAuth] = 5, [ServiceId.Updates] = 2,
            [ServiceId.Onliner] = 12, [ServiceId.CloudMessaging] = 5, [ServiceId.Web] = 2,
            [ServiceId.Developers] = 4, [ServiceId.Calls] = 10, [ServiceId.Bots] = 13,
            [ServiceId.Federation] = 33
        };
        Assert.Equal(expectedCounts, SettingsCatalog.All.GroupBy(entry => entry.ServiceId)
            .ToDictionary(group => group.Key, group => group.Count()));

        foreach (var entry in SettingsCatalog.All)
        {
            Assert.Same(entry, SettingsCatalog.Resolve(entry.ServiceId, entry.Section, entry.Key));
            Assert.Same(entry, SettingsCatalog.Resolve(entry.ServiceId, entry.StorageKey));
        }

        Assert.True(SettingsCatalog.Resolve(ServiceId.Calls, "LiveKit:ApiKey").IsSensitive);
        Assert.True(SettingsCatalog.Resolve(ServiceId.Calls, "LiveKit:ApiSecret").IsSensitive);

        var snapshot = string.Join('\n', SettingsCatalog.All
            .OrderBy(entry => entry.ServiceId)
            .ThenBy(entry => entry.Section, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{(int)entry.ServiceId}|{entry.Section}|{entry.Key}|{entry.StorageKey}|{entry.IsSensitive}|{entry.RequiresManualValue}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        Assert.Equal("3FED757F8F442BDF2591E776AC69048C6EC27858C35ED810A8A4739FF1C467C8", hash);
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
