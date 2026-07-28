using Barkfluff.AdminPanel.Services;

using System.Text.Json;
using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class ServiceMetricsEventParserTests
{
    [Fact]
    public void TryParse_ReadsObjectPropertiesSchemaV2()
    {
        using var document = JsonDocument.Parse("""
            { "Timestamp":"2026-07-29T10:00:00Z", "Properties": {
              "Application":"BarkFluff.Identity", "Metrics": {
                "SchemaVersion":2, "ServiceName":"BarkFluff.Identity",
                "Counters":{"auth_login_success":1}, "Gauges":{"online":3}
              }}}
            """);

        Assert.True(ServiceMetricsEventParser.TryParse(document.RootElement, out var metric));
        Assert.Equal("BarkFluff.Identity", metric.ServiceName);
        Assert.Equal(1, metric.Counters["auth_login_success"]);
        Assert.Equal(3, metric.Gauges["online"]);
    }

    [Fact]
    public void TryParse_ReadsArrayPropertiesWithNestedMetrics()
    {
        using var document = JsonDocument.Parse("""
            { "Timestamp":"2026-07-29T10:00:00Z", "Properties": [
              {"Name":"Application","Value":"BarkFluff.Files"},
              {"Name":"Metrics","Value":{"Metrics":{"SchemaVersion":2,"ServiceName":"BarkFluff.Files","Counters":{"files_uploaded":2},"Gauges":{}}}}
            ]}
            """);

        Assert.True(ServiceMetricsEventParser.TryParse(document.RootElement, out var metric));
        Assert.Equal("BarkFluff.Files", metric.ServiceName);
        Assert.Equal(2, metric.Counters["files_uploaded"]);
    }
}
