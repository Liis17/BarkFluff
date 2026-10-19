using System.Net;

using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Host;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace BarkFluff.Settings.Tests.Infrastructure;

public sealed class ConfigurationReadinessEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-color")]
    public async Task Missing_or_invalid_manual_value_blocks_services_but_not_admin(string? invalidValue)
    {
        await using var app = await StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettingsContext>();
            await FillAsync(context);
            var row = await context.Settings(SettingsScopes.Get(ServiceId.Beacon))
                .SingleAsync(row => row.Key == "ServerColor:Main");
            if (invalidValue is null) context.Remove(row);
            else row.Value = invalidValue;
            await context.SaveChangesAsync();
        }

        using var client = app.GetTestClient();
        using var strict = await client.GetAsync("/internal/configuration/ready");
        using var admin = await client.GetAsync("/internal/configuration/ready?requireValidSettings=false");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, strict.StatusCode);
        Assert.Equal(string.Empty, await strict.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_settings_are_ready_without_complete_setup_and_ignore_disabled_channels(bool telegramOnly)
    {
        await using var app = await StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettingsContext>();
            await FillAsync(context);
            if (telegramOnly)
            {
                foreach (var row in await context.Settings(SettingsScopes.Get(ServiceId.Unknown))
                             .Where(row => row.Key.StartsWith("Email:")).ToListAsync())
                    row.Value = row.Key == "Email:Enabled" ? "false" : string.Empty;
                var identity = context.Settings(SettingsScopes.Get(ServiceId.Identity));
                (await identity.SingleAsync(row => row.Key == "TelegramAuth:Enabled")).Value = "true";
                (await identity.SingleAsync(row => row.Key == "TelegramAuth:BotToken")).Value = "test-token";
                (await identity.SingleAsync(row => row.Key == "TelegramAuth:NodeName")).Value = "Test node";
                await context.SaveChangesAsync();
            }
            Assert.False(await context.SetupStates.AnyAsync());
        }

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/internal/configuration/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single();
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unreadable_database_blocks_both_modes_without_exposing_exception(bool strict)
    {
        // An isolated SQLite database with no schema simulates unreadable settings tables.
        await using var app = await StartAsync(unreadableDatabase: true);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync($"/internal/configuration/ready?requireValidSettings={strict}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rechecks_current_values_instead_of_caching_a_previous_failure()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var initial = await client.GetAsync("/internal/configuration/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, initial.StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
            await FillAsync(scope.ServiceProvider.GetRequiredService<SettingsContext>());

        using var ready = await client.GetAsync("/internal/configuration/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(bool unreadableDatabase = false)
    {
        var database = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<SettingsContext>(options =>
        {
            if (unreadableDatabase) options.UseSqlite("Data Source=:memory:");
            else options.UseInMemoryDatabase(database);
        });
        builder.Services.AddScoped<SettingsReadinessContributor>();
        var app = builder.Build();
        app.MapConfigurationReadiness();
        await app.StartAsync();
        return app;
    }

    private static async Task FillAsync(SettingsContext context)
    {
        await new SettingsSeeder(context, new SettingsSeedOptions("postgres", "postgres", "postgres", "guest", "guest")).SeedAsync();
        foreach (var entry in SettingsCatalog.All.Where(entry => entry.RequiresManualValue
                     && entry.Setup is not null
                     && entry.Setup.Requirement is not (SetupRequirement.FederationEnabled or SetupRequirement.TelegramEnabled)))
        {
            var row = await context.Settings(SettingsScopes.Get(entry.ServiceId)).SingleAsync(row => row.Key == entry.StorageKey);
            row.Value = entry.Setup!.InputType switch
            {
                SetupInputType.Color => "#112233",
                SetupInputType.Host => "smtp.example.com",
                SetupInputType.Integer => "587",
                SetupInputType.Email => "noreply@example.com",
                SetupInputType.Url => "https://files.example.com",
                _ => "test-value"
            };
        }
        await context.SaveChangesAsync();
    }
}
