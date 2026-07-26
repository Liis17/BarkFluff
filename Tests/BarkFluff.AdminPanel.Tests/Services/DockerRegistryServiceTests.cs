using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;

using System.Net;
using System.Net.Http;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class DockerRegistryServiceTests
{
    [Fact]
    public async Task GetVersionStatusAsync_UsesHighestSemverAndIgnoresNonSemverTags()
    {
        var service = CreateService("{\"name\":\"barkfluff-users-dev\",\"tags\":[\"1.0.9\",\"latest\",\"1.0.10\",\"3331878f5f4f\",\"invalid\"]}");

        var result = await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users-dev:1.0.9");

        Assert.Equal("1.0.9", result.CurrentVersion);
        Assert.Equal("1.0.10", result.LatestVersion);
        Assert.True(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionStatusAsync_KeepsDevRepositoryWhenLookingUpTags()
    {
        string? requestedPath = null;
        var service = CreateService("{\"tags\":[\"1.0.10\"]}", request => requestedPath = request.RequestUri?.AbsolutePath);

        await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users-dev:1.0.9");

        Assert.Equal("/v2/barkfluff-users-dev/tags/list", requestedPath);
    }

    [Theory]
    [InlineData("1.0.9", true)]
    [InlineData("1.0.10", false)]
    [InlineData("1.1.0", false)]
    public async Task GetVersionStatusAsync_ComparesCurrentVersionWithLatest(string currentVersion, bool updateAvailable)
    {
        var service = CreateService("{\"tags\":[\"1.0.10\",\"latest\"]}");

        var result = await service.GetVersionStatusAsync($"docker.barkfluff.com:5000/barkfluff-users:{currentVersion}");

        Assert.Equal("1.0.10", result.LatestVersion);
        Assert.Equal(updateAvailable, result.UpdateAvailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetVersionStatusAsync_ReturnsUnavailableStatusWhenRegistryFails(HttpStatusCode statusCode)
    {
        var service = CreateService("", responseStatus: statusCode);

        var result = await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users:1.0.9");

        Assert.Null(result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionStatusAsync_ReturnsUnavailableStatusForNonSemverCurrentTag()
    {
        var service = CreateService("{\"tags\":[\"1.0.10\"]}");

        var result = await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users:latest");

        Assert.Null(result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionStatusAsync_ReturnsUnavailableStatusWhenRegistryHasNoSemverTags()
    {
        var service = CreateService("{\"tags\":[\"latest\",\"3331878f5f4f\"]}");

        var result = await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users:1.0.9");

        Assert.Null(result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task GetVersionStatusAsync_ReturnsUnavailableStatusWhenRequestThrows()
    {
        var service = CreateService("", _ => throw new HttpRequestException("Network unavailable"));

        var result = await service.GetVersionStatusAsync("docker.barkfluff.com:5000/barkfluff-users:1.0.9");

        Assert.Null(result.CurrentVersion);
        Assert.Null(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
    }

    private static DockerRegistryService CreateService(
        string body,
        Action<HttpRequestMessage>? inspectRequest = null,
        HttpStatusCode responseStatus = HttpStatusCode.OK)
    {
        var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            inspectRequest?.Invoke(request);
            return new HttpResponseMessage(responseStatus)
            {
                Content = new StringContent(body)
            };
        }))
        {
            BaseAddress = new Uri("https://docker.barkfluff.com:5000")
        };

        return new DockerRegistryService(client, NullLogger<DockerRegistryService>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
