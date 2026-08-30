using BarkFluff.Configuration.Domain;
using BarkFluff.Configuration.Persistence;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Configuration.Tests.Persistence;

public class ConfigurationStorageTests
{
    [Fact]
    public async Task UpdateConfiguration_CreatesRevisionWithPreviousAndNewValues()
    {
        await using var context = CreateContext();
        var storage = new ConfigurationStorage(context, new MetricsCollector());

        await storage.UpdateConfigurationAsync("Feature", "Enabled", "true", ServiceId.Users, "admin", "test");
        await storage.UpdateConfigurationAsync("Feature", "Enabled", "false", ServiceId.Users, "admin", "test");

        var revisions = await storage.GetConfigurationHistoryAsync("Feature", "Enabled", ServiceId.Users, 30);

        Assert.Equal(2, revisions.Count);
        Assert.Equal("true", revisions[0].PreviousValue);
        Assert.Equal("false", revisions[0].NewValue);
    }

    [Fact]
    public async Task RollbackConfiguration_RestoresPreviousValueAndRecordsRollback()
    {
        await using var context = CreateContext();
        var storage = new ConfigurationStorage(context, new MetricsCollector());

        await storage.UpdateConfigurationAsync("Feature", "Enabled", "true", ServiceId.Users, "admin", "test");
        await storage.UpdateConfigurationAsync("Feature", "Enabled", "false", ServiceId.Users, "admin", "test");
        var source = (await storage.GetConfigurationHistoryAsync("Feature", "Enabled", ServiceId.Users, 1)).Single();

        await storage.RollbackConfigurationAsync(source.Id, "reviewer", "test");

        var item = await context.Configurations.SingleAsync();
        var revisions = await storage.GetConfigurationHistoryAsync("Feature", "Enabled", ServiceId.Users, 30);
        Assert.Equal("true", item.Value);
        Assert.Equal(3, revisions.Count);
        Assert.Equal("Rollback", revisions[0].ChangeKind);
        Assert.Equal(source.Id, revisions[0].SourceRevisionId);
        Assert.Equal("false", revisions[0].PreviousValue);
        Assert.Equal("true", revisions[0].NewValue);
    }

    [Fact]
    public async Task UpdateConfiguration_WithEmptyKey_UpdatesExistingRowAndCreatesRevision()
    {
        await using var context = CreateContext();
        context.Configurations.Add(new ConfigurationItem
        {
            Section = "DevelopersDb",
            Key = "",
            Value = "Host=old;Database=developers",
            EditedAt = DateTime.UtcNow,
            EditedBy = "system",
            EditedFrom = "migration",
            ServiceId = ServiceId.Developers
        });
        await context.SaveChangesAsync();

        var storage = new ConfigurationStorage(context, new MetricsCollector());

        await storage.UpdateConfigurationAsync(
            "DevelopersDb",
            "",
            "Host=new;Database=developers",
            ServiceId.Developers,
            "admin",
            "test");

        var item = await context.Configurations.SingleAsync();
        var revisions = await storage.GetConfigurationHistoryAsync(
            "DevelopersDb",
            "",
            ServiceId.Developers,
            30);

        Assert.Equal("Host=new;Database=developers", item.Value);
        Assert.Single(revisions);
        Assert.Equal("Host=old;Database=developers", revisions[0].PreviousValue);
        Assert.Equal("Host=new;Database=developers", revisions[0].NewValue);
    }

    [Fact]
    public async Task UpdateConfiguration_WithEmptyKey_RepairsWhitespaceOnlySectionKey()
    {
        await using var context = CreateContext();
        context.Configurations.Add(new ConfigurationItem
        {
            Section = "DevelopersDb",
            Key = " ",
            Value = "Host=old;Database=developers",
            EditedAt = DateTime.UtcNow,
            EditedBy = "system",
            EditedFrom = "legacy",
            ServiceId = ServiceId.Developers
        });
        await context.SaveChangesAsync();

        var storage = new ConfigurationStorage(context, new MetricsCollector());

        await storage.UpdateConfigurationAsync(
            "DevelopersDb",
            "",
            "Host=new;Database=developers",
            ServiceId.Developers,
            "admin",
            "test");

        var item = await context.Configurations.SingleAsync();
        var revisions = await storage.GetConfigurationHistoryAsync(
            "DevelopersDb",
            "",
            ServiceId.Developers,
            30);

        Assert.Equal("", item.Key);
        Assert.Equal("Host=new;Database=developers", item.Value);
        Assert.Single(revisions);
        Assert.Equal("Host=old;Database=developers", revisions[0].PreviousValue);
        Assert.Equal("Host=new;Database=developers", revisions[0].NewValue);
    }

    [Fact]
    public async Task GetAllConfigurations_CanonicalizesWhitespaceOnlySectionKey()
    {
        await using var context = CreateContext();
        context.Configurations.Add(new ConfigurationItem
        {
            Section = "DevelopersDb",
            Key = "\t",
            Value = "Host=postgres;Database=developers",
            EditedAt = DateTime.UtcNow,
            EditedBy = "system",
            EditedFrom = "legacy",
            ServiceId = ServiceId.Developers
        });
        await context.SaveChangesAsync();

        var configurations = await new ConfigurationStorage(context, new MetricsCollector())
            .GetAllConfigurationsAsync();

        Assert.Equal("", Assert.Single(configurations).Key);
    }

    private static ConfigurationContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConfigurationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ConfigurationContext(options);
    }
}
