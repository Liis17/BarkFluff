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
            [ServiceId.Unknown] = 23, [ServiceId.Identity] = 20, [ServiceId.Users] = 10,
            [ServiceId.Beacon] = 13, [ServiceId.Notifications] = 8, [ServiceId.Files] = 55,
            [ServiceId.Messages] = 9, [ServiceId.FastAuth] = 8, [ServiceId.Updates] = 5,
            [ServiceId.Onliner] = 15, [ServiceId.CloudMessaging] = 8, [ServiceId.Web] = 5,
            [ServiceId.Developers] = 7, [ServiceId.Calls] = 13, [ServiceId.Bots] = 16,
            [ServiceId.Federation] = 36
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
        Assert.True(hash == "2FEBA90B9FCC3319A60E5E106FCFF7B7B14A200221265FBE0AFA42A7C87E8875", $"Catalog fingerprint: {hash}");
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

    [Fact]
    public void Logging_flags_are_scoped_per_service_and_legacy_backend_is_not_active()
    {
        var serviceScopes = SettingsScopes.All.Where(scope => scope.ServiceId != ServiceId.Unknown).ToArray();
        Assert.Equal(15, serviceScopes.Length);
        var seedValues = new SettingsSeedValues("", "", "", "", "", "", "", "", _ => "");

        foreach (var scope in serviceScopes)
        {
            Assert.Equal("false", SettingsCatalog.Resolve(scope.ServiceId, "LognaraEnable", "").DefaultFactory(seedValues));
            Assert.Equal("true", SettingsCatalog.Resolve(scope.ServiceId, "SeqEnable", "").DefaultFactory(seedValues));
            Assert.Equal("true", SettingsCatalog.Resolve(scope.ServiceId, "ConsoleOutput", "").DefaultFactory(seedValues));
        }

        Assert.DoesNotContain(SettingsCatalog.All, entry => entry.StorageKey == "Logging:Backend");
        Assert.DoesNotContain(SettingsCatalog.All, entry =>
            entry.ServiceId == ServiceId.Unknown && entry.StorageKey is "LognaraEnable" or "SeqEnable" or "ConsoleOutput");
        Assert.True(SettingsCatalog.IsDeprecated(SettingsCatalog.Resolve(ServiceId.Unknown, "Logging:Backend")));
        Assert.Throws<UnknownSettingException>(() => SettingsCatalog.ResolveForUpdate(ServiceId.Unknown, "Logging", "Backend"));
    }
}
