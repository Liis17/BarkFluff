using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Settings.Tests.Infrastructure;

public sealed class SettingsReadinessContributorTests
{
    [Fact]
    public async Task Reports_degraded_with_sorted_manual_keys_until_they_are_filled()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();
        var contributor = new SettingsReadinessContributor(context);

        var check = await contributor.CheckAsync();

        Assert.Equal("degraded", check.Status);
        Assert.NotNull(check.Error);
        var keys = check.Error!["manual settings are empty: ".Length..].Split(", ");
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
    }

    [Fact]
    public async Task Disabled_federation_does_not_block_readiness()
    {
        await using var context = CreateContext();
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();

        var check = await new SettingsReadinessContributor(context).CheckAsync();

        Assert.DoesNotContain("FederationSettings:Federation:ServerName", check.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("FederationSettings:Federation:ExternalEndpoint", check.Error ?? string.Empty, StringComparison.Ordinal);
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }
}
