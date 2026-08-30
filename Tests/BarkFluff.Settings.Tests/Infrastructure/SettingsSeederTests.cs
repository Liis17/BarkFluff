using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Settings.Tests.Infrastructure;

public sealed class SettingsSeederTests
{
    [Fact]
    public async Task Seed_is_idempotent_and_does_not_replace_existing_values()
    {
        await using var context = CreateContext();
        var scope = SettingsScopes.Get(ServiceId.Identity);
        context.Settings(scope).Add(new SettingRow
        {
            Key = "RunSettings:Port",
            Value = "9999",
            EditedBy = "operator",
            EditedAt = DateTime.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();
        var seeder = new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest"));

        var first = await seeder.SeedAsync();
        var second = await seeder.SeedAsync();

        Assert.True(first > 0);
        Assert.Equal(0, second);
        var existing = await context.Settings(scope).SingleAsync(row => row.Key == "RunSettings:Port");
        Assert.Equal("9999", existing.Value);
        Assert.Equal("operator", existing.EditedBy);
        Assert.Empty(await context.SettingsHistory.ToListAsync());
    }

    [Fact]
    public async Task Seed_creates_manual_values_empty_and_generated_secrets_once()
    {
        await using var context = CreateContext();
        var seeder = new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest"));

        await seeder.SeedAsync();

        var global = SettingsScopes.Get(ServiceId.Unknown);
        var secret = await context.Settings(global).SingleAsync(row => row.Key == "JwtSettings:SecretKey");
        var token = await context.Settings(global).SingleAsync(row => row.Key == "UsersService:Token");
        var federation = SettingsScopes.Get(ServiceId.Federation);
        var manual = await context.Settings(federation).SingleAsync(row => row.Key == "Federation:ServerName");
        Assert.Equal(64, secret.Value.Length);
        Assert.NotEmpty(token.Value);
        Assert.Empty(manual.Value);

        var oldSecret = secret.Value;
        await seeder.SeedAsync();
        Assert.Equal(oldSecret, secret.Value);
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }
}
