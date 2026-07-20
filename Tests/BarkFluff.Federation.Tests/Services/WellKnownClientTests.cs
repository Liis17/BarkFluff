using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Services;

// Сетевой путь FetchAsync (реальный TLS-фетч) — уровень двух-нодового стенда; здесь — ветки,
// завершающиеся до сети: синтаксис и анти-SSRF.
public class WellKnownClientTests
{
    private static WellKnownClient CreateClient(ServernameValidator? validator = null)
        => new(
            validator ?? new ServernameValidator(),
            Tests.Infrastructure.TestHelpers.CreateConfiguration(),
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production),
            new MetricsCollector(),
            NullLogger<WellKnownClient>.Instance);

    [Theory]
    [InlineData("!!!")]
    [InlineData("")]
    public async Task FetchAsync_InvalidSyntax_ReturnsNullWithoutNetwork(string servername)
    {
        var client = CreateClient();

        var document = await client.FetchAsync(servername);

        document.Should().BeNull();
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.10")]
    public async Task FetchAsync_PrivateOrInvalidHost_RejectedByAntiSsrf(string servername)
    {
        // localhost/IP-литералы не проходят TryNormalizeSyntax — возврат null до DNS и HTTP.
        var client = CreateClient();

        var document = await client.FetchAsync(servername);

        document.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_HostNotResolving_ReturnsNull()
    {
        // Реальный ServernameValidator: несуществующий TLD не резолвится → null (DNS-ошибка, не HTTP).
        // Имя заведомо валидно синтаксически, но .invalid зарезервирован RFC 2606 и не резолвится.
        var client = CreateClient();

        var document = await client.FetchAsync("nonexistent-host-for-tests.invalid");

        document.Should().BeNull();
    }
}
