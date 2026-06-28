using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Infrastructure;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Infrastructure;

public class LocationClientExtensionsTests
{
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<LocationClient>> _logger;

    public LocationClientExtensionsTests()
    {
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<LocationClient>>();
    }

    [Fact]
    public async Task GetLocationString_NullIp_ReturnsDash()
    {
        var httpClient = new HttpClient();
        var client = new LocationClient(httpClient, _metrics, _logger.Object);

        var result = await client.GetLocationString(null);

        Assert.Equal("-", result);
    }

    [Fact]
    public async Task GetLocationString_EmptyIp_ReturnsDash()
    {
        var httpClient = new HttpClient();
        var client = new LocationClient(httpClient, _metrics, _logger.Object);

        var result = await client.GetLocationString("");

        Assert.Equal("-", result);
    }
}
