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

    private static ConfigurationContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConfigurationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ConfigurationContext(options);
    }
}
