using BarkFluff.Configuration.Domain;
using BarkFluff.Configuration.Infrastructure;
using BarkFluff.Configuration.Persistence;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace BarkFluff.Configuration.Tests.Infrastructure;

public class ConfigurationDefaultsPopulatorTests
{
    private const string LiveKitApiSecret = "devsecret_change_me_in_production_0123456789";

    [Fact]
    public async Task PopulateDefaults_FillsDevelopersAndLiveKitDefaults()
    {
        await using var context = CreateContext();

        AddEmpty(context, ServiceId.Developers, "RunSettings", "Port");
        AddEmpty(context, ServiceId.Developers, "RunSettings", "Http1Port");
        AddEmpty(context, ServiceId.Developers, "DevelopersDb", "");
        AddEmpty(context, ServiceId.Developers, "ExternalEndpoint", "Host");
        AddEmpty(context, ServiceId.Calls, "LiveKit", "ApiKey");
        AddEmpty(context, ServiceId.Calls, "LiveKit", "ApiSecret");

        await CreatePopulator(context).PopulateDefaultsAsync();

        Assert.Equal("7020", GetValue(context, ServiceId.Developers, "RunSettings", "Port"));
        Assert.Equal("7021", GetValue(context, ServiceId.Developers, "RunSettings", "Http1Port"));
        Assert.Equal(
            "Host=postgres;Database=developers;Username=postgres;Password=postgres",
            GetValue(context, ServiceId.Developers, "DevelopersDb", ""));
        Assert.Equal("https://developers.example.com", GetValue(context, ServiceId.Developers, "ExternalEndpoint", "Host"));
        Assert.Equal("devkey", GetValue(context, ServiceId.Calls, "LiveKit", "ApiKey"));
        Assert.Equal(LiveKitApiSecret, GetValue(context, ServiceId.Calls, "LiveKit", "ApiSecret"));
    }

    [Fact]
    public async Task PopulateDefaults_DoesNotOverwriteOperatorValues()
    {
        await using var context = CreateContext();

        AddEmpty(context, ServiceId.Developers, "RunSettings", "Port");
        Add(context, ServiceId.Developers, "DevelopersDb", "", "Host=db;Database=custom;Username=custom;Password=custom");
        Add(context, ServiceId.Calls, "LiveKit", "ApiKey", "operator-key");
        Add(context, ServiceId.Calls, "LiveKit", "ApiSecret", "operator-secret");

        await CreatePopulator(context).PopulateDefaultsAsync();

        Assert.Equal("Host=db;Database=custom;Username=custom;Password=custom", GetValue(context, ServiceId.Developers, "DevelopersDb", ""));
        Assert.Equal("operator-key", GetValue(context, ServiceId.Calls, "LiveKit", "ApiKey"));
        Assert.Equal("operator-secret", GetValue(context, ServiceId.Calls, "LiveKit", "ApiSecret"));
    }

    private static ConfigurationDefaultsPopulator CreatePopulator(ConfigurationContext context) =>
        new(
            context,
            NullLogger<ConfigurationDefaultsPopulator>.Instance,
            "postgres",
            "postgres",
            "postgres",
            "rabbit",
            "rabbit",
            new MetricsCollector());

    private static ConfigurationContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConfigurationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ConfigurationContext(options);
    }

    private static void AddEmpty(ConfigurationContext context, ServiceId serviceId, string section, string key) =>
        Add(context, serviceId, section, key, string.Empty);

    private static void Add(ConfigurationContext context, ServiceId serviceId, string section, string key, string value)
    {
        context.Configurations.Add(new ConfigurationItem
        {
            Section = section,
            Key = key,
            Value = value,
            EditedAt = DateTime.UtcNow,
            EditedBy = "test",
            EditedFrom = "test",
            ServiceId = serviceId
        });
        context.SaveChanges();
    }

    private static string GetValue(ConfigurationContext context, ServiceId serviceId, string section, string key) =>
        context.Configurations.Single(c => c.ServiceId == serviceId && c.Section == section && c.Key == key).Value;
}
