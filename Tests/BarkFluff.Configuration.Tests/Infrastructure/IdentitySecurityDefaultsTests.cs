using BarkFluff.Configuration.Domain;
using BarkFluff.Configuration.Infrastructure;
using BarkFluff.Configuration.Persistence;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace BarkFluff.Configuration.Tests.Infrastructure;

public class IdentitySecurityDefaultsTests
{
    [Fact]
    public async Task PopulateDefaults_FillsIdentityRedisAndSecurityDefaults()
    {
        await using var context = new ConfigurationContext(new DbContextOptionsBuilder<ConfigurationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

        AddEmpty(context, "Redis", "");
        foreach (var key in new[]
        {
            "HighRiskRequestsPerMinute",
            "SubjectRequestsPerWindow",
            "SubjectWindowMinutes",
            "FailureLimit",
            "FailureWindowMinutes",
            "LockoutMinutes",
            "CodeAttemptLimit",
            "OtpAttemptLimit",
            "BackoffBaseMilliseconds",
            "BackoffMaxMilliseconds"
        })
        {
            AddEmpty(context, "IdentitySecurity", key);
        }

        await CreatePopulator(context).PopulateDefaultsAsync();

        Assert.Equal("redis:6379", GetValue(context, "Redis", ""));
        Assert.Equal("60", GetValue(context, "IdentitySecurity", "HighRiskRequestsPerMinute"));
        Assert.Equal("5", GetValue(context, "IdentitySecurity", "SubjectRequestsPerWindow"));
        Assert.Equal("15", GetValue(context, "IdentitySecurity", "SubjectWindowMinutes"));
        Assert.Equal("5", GetValue(context, "IdentitySecurity", "FailureLimit"));
        Assert.Equal("15", GetValue(context, "IdentitySecurity", "FailureWindowMinutes"));
        Assert.Equal("15", GetValue(context, "IdentitySecurity", "LockoutMinutes"));
        Assert.Equal("5", GetValue(context, "IdentitySecurity", "CodeAttemptLimit"));
        Assert.Equal("5", GetValue(context, "IdentitySecurity", "OtpAttemptLimit"));
        Assert.Equal("250", GetValue(context, "IdentitySecurity", "BackoffBaseMilliseconds"));
        Assert.Equal("2000", GetValue(context, "IdentitySecurity", "BackoffMaxMilliseconds"));
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

    private static void AddEmpty(ConfigurationContext context, string section, string key)
    {
        context.Configurations.Add(new ConfigurationItem
        {
            Section = section,
            Key = key,
            Value = string.Empty,
            EditedAt = DateTime.UtcNow,
            EditedBy = "test",
            EditedFrom = "test",
            ServiceId = ServiceId.Identity
        });
        context.SaveChanges();
    }

    private static string GetValue(ConfigurationContext context, string section, string key) =>
        context.Configurations.Single(c =>
            c.ServiceId == ServiceId.Identity && c.Section == section && c.Key == key).Value;
}
