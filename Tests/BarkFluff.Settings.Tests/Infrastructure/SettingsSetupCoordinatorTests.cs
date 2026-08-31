using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Settings.Tests.Infrastructure;

public sealed class SettingsSetupCoordinatorTests
{
    [Fact]
    public async Task Fresh_snapshot_requires_twelve_fields_and_skips_disabled_federation()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, SeedOptions()).SeedAsync();

        var snapshot = await new SettingsSetupCoordinator(context).GetSnapshotAsync();

        Assert.False(snapshot.Complete);
        Assert.False(snapshot.Locked);
        Assert.Equal(19, SettingsCatalog.All.Count(entry => entry.Setup is not null));
        Assert.Equal(12, snapshot.Groups.SelectMany(group => group.Fields).Count(field => field.Required));
        Assert.False(snapshot.Groups.Single(group => group.Metadata.Id == "federation").Applicable);
        Assert.All(snapshot.Groups.Single(group => group.Metadata.Id == "federation").Fields, field => Assert.False(field.Required));
    }

    [Fact]
    public async Task Complete_setup_normalizes_values_masks_secret_and_locks_current_catalog()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, SeedOptions()).SeedAsync();
        var coordinator = new SettingsSetupCoordinator(context);

        await coordinator.SaveGroupAsync("server", new Dictionary<string, string?>
        {
            [Field(ServiceId.Beacon, "ServerProps:Name")] = "  Home  ",
            [Field(ServiceId.Beacon, "ServerProps:Description")] = "Community node",
            [Field(ServiceId.Beacon, "ServerProps:PublicName")] = "Public Home",
            [Field(ServiceId.Beacon, "ServerProps:Location")] = "Moscow",
            [Field(ServiceId.Beacon, "ServerColor:Lite")] = "#aabbcc",
            [Field(ServiceId.Beacon, "ServerColor:Main")] = "112233",
            [Field(ServiceId.Beacon, "ServerColor:Hard")] = "#445566"
        }, "setup", "127.0.0.1");
        await coordinator.SaveGroupAsync("email", new Dictionary<string, string?>
        {
            [Field(ServiceId.Notifications, "Email:Host")] = "smtp.example.com",
            [Field(ServiceId.Notifications, "Email:Port")] = "587",
            [Field(ServiceId.Notifications, "Email:SenderEmail")] = "noreply@example.com",
            [Field(ServiceId.Notifications, "Email:SenderPassword")] = "super-secret"
        }, "setup", "127.0.0.1");
        await coordinator.SaveGroupAsync("media", new Dictionary<string, string?>
        {
            [Field(ServiceId.Files, "ExternalEndpoint:MediaHost")] = "https://files.example.com/"
        }, "setup", "127.0.0.1");

        var completed = await coordinator.CompleteAsync("setup", "127.0.0.1");

        Assert.True(completed.Complete);
        Assert.True(completed.Locked);
        Assert.Equal("#AABBCC", FieldValue(completed, ServiceId.Beacon, "ServerColor:Lite"));
        Assert.Equal(string.Empty, FieldValue(completed, ServiceId.Notifications, "Email:SenderPassword"));
        Assert.True(completed.Groups.SelectMany(group => group.Fields)
            .Single(field => field.StorageKey == "Email:SenderPassword").Configured);
        await Assert.ThrowsAsync<SetupLockedException>(() => coordinator.SaveGroupAsync(
            "server", new Dictionary<string, string?> { [Field(ServiceId.Beacon, "ServerProps:Name")] = "Changed" }, "setup", "127.0.0.1"));
    }

    [Fact]
    public async Task Enabling_federation_in_the_same_group_makes_its_fields_required()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, SeedOptions()).SeedAsync();
        var coordinator = new SettingsSetupCoordinator(context);

        await Assert.ThrowsAsync<SetupIncompleteException>(() => coordinator.CompleteAsync("setup", "127.0.0.1"));
        var state = await coordinator.SaveGroupAsync("federation", new Dictionary<string, string?>
        {
            [Field(ServiceId.Federation, "Federation:Enabled")] = "true",
            [Field(ServiceId.Federation, "Federation:ServerName")] = "BÜCHER.example",
            [Field(ServiceId.Federation, "Federation:ExternalEndpoint")] = "https://federation.example.com",
            [Field(ServiceId.Federation, "Federation:TlsSpkiSha256")] = Convert.ToBase64String(new byte[32]),
            [Field(ServiceId.Federation, "Federation:WellKnownPort")] = "7031",
            [Field(ServiceId.Federation, "Federation:KeyRotationOverlapDays")] = "30",
            [Field(ServiceId.Federation, "Federation:SignatureWindowSeconds")] = "300"
        }, "setup", "127.0.0.1");

        var federation = state.Groups.Single(group => group.Metadata.Id == "federation");
        Assert.True(federation.Applicable);
        Assert.All(federation.Fields, field => Assert.True(field.Configured));
        Assert.Equal("xn--bcher-kva.example", federation.Fields.Single(field => field.Key == "ServerName").Value);
    }

    private static string Field(ServiceId serviceId, string storageKey) =>
        SettingsSetupMetadata.GetFieldId(SettingsCatalog.Resolve(serviceId, storageKey));

    private static string FieldValue(SetupSnapshot snapshot, ServiceId serviceId, string storageKey) =>
        snapshot.Groups.SelectMany(group => group.Fields)
            .Single(field => field.ServiceId == serviceId && field.StorageKey == storageKey)
            .Value;

    private static SettingsSeedOptions SeedOptions() =>
        new("postgres", "postgres", "postgres", "guest", "guest");

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }
}
