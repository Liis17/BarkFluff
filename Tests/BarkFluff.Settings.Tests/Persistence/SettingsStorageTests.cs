using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Settings.Tests.Persistence;

public sealed class SettingsStorageTests
{
    [Fact]
    public async Task Service_values_override_global_values_by_configuration_path()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();
        var storage = new SettingsStorage(context, new MetricsCollector());
        await storage.UpdateAsync("ExternalEndpoint", "Host", "https://users.test", ServiceId.Users, "admin", "test");

        var result = await storage.GetConfigurationAsync(ServiceId.Users);

        Assert.Equal("https://users.test", result.Single(item => item.StorageKey == "ExternalEndpoint:Host").Value);
        Assert.Contains(result, item => item.StorageKey == "JwtSettings:Issuer" && item.ServiceId == ServiceId.Unknown);
    }

    [Fact]
    public async Task Update_and_rollback_write_atomic_revisions_with_source_link()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();
        var storage = new SettingsStorage(context, new MetricsCollector());
        var initial = (await storage.GetConfigurationAsync(ServiceId.Users))
            .Single(item => item.StorageKey == "RunSettings:Port").Value;

        await storage.UpdateAsync("RunSettings", "Port", "9001", ServiceId.Users, "alice", "AdminPanel");
        var update = Assert.Single(await storage.GetHistoryAsync("RunSettings", "Port", ServiceId.Users, 30));
        Assert.Equal(initial, update.PreviousValue);
        Assert.Equal("9001", update.NewValue);
        Assert.Equal("Update", update.ChangeKind);

        await storage.RollbackAsync(update.Id, "bob", "AdminPanel");

        var history = await storage.GetHistoryAsync("RunSettings", "Port", ServiceId.Users, 30);
        Assert.Equal(2, history.Count);
        Assert.Equal("Rollback", history[0].ChangeKind);
        Assert.Equal(update.Id, history[0].SourceRevisionId);
        Assert.Equal(initial, history[0].NewValue);
        var current = (await storage.GetConfigurationAsync(ServiceId.Users))
            .Single(item => item.StorageKey == "RunSettings:Port");
        Assert.Equal(initial, current.Value);
        Assert.Equal("AdminPanel", current.EditedFrom);
    }

    [Fact]
    public async Task Unknown_key_is_rejected_and_reserved_names_are_normalized()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();
        var storage = new SettingsStorage(context, new MetricsCollector());

        await Assert.ThrowsAsync<UnknownSettingException>(() =>
            storage.UpdateAsync("Custom", "Key", "value", ServiceId.Users, "alice", "test"));
        await storage.AddReservedNameAsync("  MyName  ");
        Assert.Contains("myname", await storage.GetReservedNamesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.AddReservedNameAsync("MYNAME"));
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }
}
