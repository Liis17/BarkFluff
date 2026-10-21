using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Settings.Settings;
using BarkFluff.Shared.Identity;

using System.IdentityModel.Tokens.Jwt;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
        var messagesHost = await context.Settings(global).SingleAsync(row => row.Key == "MessagesService:Host");
        var messagesToken = await context.Settings(global).SingleAsync(row => row.Key == "MessagesService:Token");
        var federation = SettingsScopes.Get(ServiceId.Federation);
        var manual = await context.Settings(federation).SingleAsync(row => row.Key == "Federation:ServerName");
        var files = SettingsScopes.Get(ServiceId.Files);
        var storageKey = await context.Settings(files).SingleAsync(row => row.Key == "S3Buckets:barkfluff-uploads:AccessKey");
        var calls = SettingsScopes.Get(ServiceId.Calls);
        var liveKitKey = await context.Settings(calls).SingleAsync(row => row.Key == "LiveKit:ApiKey");
        Assert.Equal(64, secret.Value.Length);
        Assert.NotEmpty(token.Value);
        Assert.Equal("http://messages:7007", messagesHost.Value);
        Assert.NotEmpty(messagesToken.Value);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(messagesToken.Value).Claims,
            claim => claim.Type == "service-name" && claim.Value == "MessagesServiceClient");
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
    public async Task Seed_preserves_all_existing_storage_and_livekit_credentials()
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
        Assert.Equal("minioadmin", storageAccess.Value);
        Assert.Equal("operator-secret", storageSecret.Value);
        Assert.Equal("devkey", liveKitKey.Value);
        Assert.Equal("operator-livekit-secret", liveKitSecret.Value);
    }

    [Fact]
    public async Task Seed_uses_optional_node_environment_values_for_new_defaults()
    {
        await using var context = CreateContext();
        var options = CreateConfiguredSeedOptions(new Dictionary<string, string?>
        {
            ["NODE_PUBLIC_DOMAIN"] = "node.example.net",
            ["NODE_NAVIGATOR_URL"] = "https://navigator.node.example.net",
            ["NODE_S3_ENDPOINT"] = "https://s3.node.example.net"
        });

        await new SettingsSeeder(context, options).SeedAsync();

        Assert.Equal("https://identity.node.example.net", await GetValue(context, ServiceId.Identity, "ExternalEndpoint:Host"));
        Assert.Equal("https://files.node.example.net", await GetValue(context, ServiceId.Files, "ExternalEndpoint:Host"));
        Assert.Equal("wss://livekit.node.example.net", await GetValue(context, ServiceId.Calls, "LiveKit:PublicUrl"));
        Assert.Equal("https://navigator.node.example.net", await GetValue(context, ServiceId.Beacon, "NavigatorUrl"));
        Assert.Equal("https://navigator.node.example.net", await GetValue(context, ServiceId.Federation, "NavigatorUrl"));

        var serviceUrls = await context.Settings(SettingsScopes.Get(ServiceId.Files))
            .Where(row => row.Key.StartsWith("S3Buckets:") && row.Key.EndsWith(":ServiceUrl"))
            .Select(row => row.Value)
            .ToListAsync();
        Assert.Equal(8, serviceUrls.Count);
        Assert.All(serviceUrls, value => Assert.Equal("https://s3.node.example.net", value));
    }

    [Fact]
    public async Task Seed_without_node_environment_values_keeps_legacy_defaults()
    {
        await using var context = CreateContext();

        await new SettingsSeeder(context, CreateConfiguredSeedOptions(new Dictionary<string, string?>())).SeedAsync();

        Assert.Equal("https://identity.example.com", await GetValue(context, ServiceId.Identity, "ExternalEndpoint:Host"));
        Assert.Equal("wss://calls.example.com", await GetValue(context, ServiceId.Calls, "LiveKit:PublicUrl"));
        Assert.Equal("http://navigator:7010", await GetValue(context, ServiceId.Beacon, "NavigatorUrl"));
        Assert.Equal("http://navigator:7010", await GetValue(context, ServiceId.Federation, "NavigatorUrl"));

        var serviceUrls = await context.Settings(SettingsScopes.Get(ServiceId.Files))
            .Where(row => row.Key.StartsWith("S3Buckets:") && row.Key.EndsWith(":ServiceUrl"))
            .Select(row => row.Value)
            .ToListAsync();
        Assert.Equal(8, serviceUrls.Count);
        Assert.All(serviceUrls, value => Assert.Equal("http://minio:9000", value));
    }

    [Fact]
    public async Task Seed_does_not_replace_existing_public_addresses_with_node_environment_values()
    {
        await using var context = CreateContext();
        context.Settings(SettingsScopes.Get(ServiceId.Identity)).Add(new SettingRow
        {
            Key = "ExternalEndpoint:Host",
            Value = "https://identity.saved.example",
            EditedBy = "operator",
            EditedAt = DateTime.UtcNow
        });
        context.Settings(SettingsScopes.Get(ServiceId.Files)).AddRange(
            new SettingRow
            {
                Key = "ExternalEndpoint:MediaHost",
                Value = "https://media.saved.example",
                EditedBy = "operator",
                EditedAt = DateTime.UtcNow
            },
            new SettingRow
            {
                Key = "S3Buckets:barkfluff-uploads:ServiceUrl",
                Value = "https://s3.saved.example",
                EditedBy = "operator",
                EditedAt = DateTime.UtcNow
            });
        context.Settings(SettingsScopes.Get(ServiceId.Calls)).Add(new SettingRow
        {
            Key = "LiveKit:PublicUrl",
            Value = "wss://livekit.saved.example",
            EditedBy = "operator",
            EditedAt = DateTime.UtcNow
        });
        context.Settings(SettingsScopes.Get(ServiceId.Beacon)).Add(new SettingRow
        {
            Key = "NavigatorUrl",
            Value = "https://navigator.saved.example",
            EditedBy = "operator",
            EditedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await new SettingsSeeder(context, CreateConfiguredSeedOptions(new Dictionary<string, string?>
        {
            ["NODE_PUBLIC_DOMAIN"] = "node.example.net",
            ["NODE_NAVIGATOR_URL"] = "https://navigator.node.example.net",
            ["NODE_S3_ENDPOINT"] = "https://s3.node.example.net"
        })).SeedAsync();

        Assert.Equal("https://identity.saved.example", await GetValue(context, ServiceId.Identity, "ExternalEndpoint:Host"));
        Assert.Equal("https://media.saved.example", await GetValue(context, ServiceId.Files, "ExternalEndpoint:MediaHost"));
        Assert.Equal("https://s3.saved.example", await GetValue(context, ServiceId.Files, "S3Buckets:barkfluff-uploads:ServiceUrl"));
        Assert.Equal("wss://livekit.saved.example", await GetValue(context, ServiceId.Calls, "LiveKit:PublicUrl"));
        Assert.Equal("https://navigator.saved.example", await GetValue(context, ServiceId.Beacon, "NavigatorUrl"));
    }

    private static SettingsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SettingsContext(options);
    }

    private static SettingsSeedOptions CreateConfiguredSeedOptions(Dictionary<string, string?> nodeVariables)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RABBITMQ_DEFAULT_USER"] = "guest",
            ["RABBITMQ_DEFAULT_PASS"] = "guest"
        };
        foreach (var (key, value) in nodeVariables)
            values[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var database = new SettingsDatabaseOptions("postgres", 5432, "settings", "settings", "settings");
        return SettingsSeedOptions.FromConfiguration(database, configuration);
    }

    private static Task<string> GetValue(SettingsContext context, ServiceId serviceId, string storageKey) =>
        context.Settings(SettingsScopes.Get(serviceId))
            .Where(row => row.Key == storageKey)
            .Select(row => row.Value)
            .SingleAsync();
}
