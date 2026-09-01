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
        var files = SettingsScopes.Get(ServiceId.Files);
        var storageKey = await context.Settings(files).SingleAsync(row => row.Key == "S3Buckets:barkfluff-uploads:AccessKey");
        var calls = SettingsScopes.Get(ServiceId.Calls);
        var liveKitKey = await context.Settings(calls).SingleAsync(row => row.Key == "LiveKit:ApiKey");
        Assert.Equal(64, secret.Value.Length);
        Assert.NotEmpty(token.Value);
        Assert.Empty(manual.Value);
        Assert.Empty(storageKey.Value);
        Assert.Empty(liveKitKey.Value);

        var oldSecret = secret.Value;
        await seeder.SeedAsync();
        Assert.Equal(oldSecret, secret.Value);
    }

    [Fact]
    public async Task Seed_creates_auto_region_for_every_s3_bucket()
    {
        await using var context = CreateContext();
        var seeder = new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest"));

        await seeder.SeedAsync();

        var regions = await context.Settings(SettingsScopes.Get(ServiceId.Files))
            .Where(row => row.Key.StartsWith("S3Buckets:") && row.Key.EndsWith(":Region"))
            .ToDictionaryAsync(row => row.Key, row => row.Value);

        Assert.Equal(8, regions.Count);
        Assert.All(regions.Values, value => Assert.Equal("auto", value));
    }

    [Fact]
    public async Task Seed_clears_legacy_storage_and_livekit_credentials_but_preserves_custom_values()
    {
        await using var context = CreateContext();
        var files = SettingsScopes.Get(ServiceId.Files);
        context.Settings(files).AddRange(
            new SettingRow
            {
                Key = "S3Buckets:barkfluff-uploads:AccessKey",
                Value = "minioadmin",
                EditedBy = "legacy",
                EditedAt = DateTime.UtcNow
            },
            new SettingRow
            {
                Key = "S3Buckets:barkfluff-uploads:SecretKey",
                Value = "operator-secret",
                EditedBy = "operator",
                EditedAt = DateTime.UtcNow
            });
        var calls = SettingsScopes.Get(ServiceId.Calls);
        context.Settings(calls).AddRange(
            new SettingRow
            {
                Key = "LiveKit:ApiKey",
                Value = "devkey",
                EditedBy = "legacy",
                EditedAt = DateTime.UtcNow
            },
            new SettingRow
            {
                Key = "LiveKit:ApiSecret",
                Value = "operator-livekit-secret",
                EditedBy = "operator",
                EditedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest"))
            .SeedAsync();

        var storageAccess = await context.Settings(files).SingleAsync(row => row.Key == "S3Buckets:barkfluff-uploads:AccessKey");
        var storageSecret = await context.Settings(files).SingleAsync(row => row.Key == "S3Buckets:barkfluff-uploads:SecretKey");
        var liveKitKey = await context.Settings(calls).SingleAsync(row => row.Key == "LiveKit:ApiKey");
        var liveKitSecret = await context.Settings(calls).SingleAsync(row => row.Key == "LiveKit:ApiSecret");
        Assert.Empty(storageAccess.Value);
        Assert.Equal("operator-secret", storageSecret.Value);
        Assert.Empty(liveKitKey.Value);
        Assert.Equal("operator-livekit-secret", liveKitSecret.Value);
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }
}
