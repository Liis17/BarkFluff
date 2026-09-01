using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class HealthCollectorServiceTests
{
    [Fact]
    public async Task Developers_health_probe_uses_http1_client()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var collector = new HealthCollectorService(
            serviceProvider,
            new DockerService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<DockerService>.Instance),
            new S3BrowserService(serviceProvider),
            httpClientFactory,
            new ConfigurationBuilder().Build(),
            NullLogger<HealthCollectorService>.Instance);

        var probeMethod = typeof(HealthCollectorService).GetMethod(
            "ProbeAllAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(probeMethod);
        var probeTask = (Task)probeMethod!.Invoke(collector, [CancellationToken.None])!;
        await probeTask;

        var developersRequests = httpClientFactory.Requests
            .Where(request => request.Uri.Host == "developers")
            .ToArray();

        Assert.NotEmpty(developersRequests);
        Assert.All(developersRequests, request =>
        {
            Assert.Equal("health-probes-http1", request.ClientName);
            Assert.Equal(HttpVersion.Version11, request.Version);
        });
    }

    private sealed record ProbeRequest(string ClientName, Uri Uri, Version Version);

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _h2cClient;
        private readonly HttpClient _http1Client;

        public ConcurrentQueue<ProbeRequest> Requests { get; } = new();

        public RecordingHttpClientFactory()
        {
            _h2cClient = CreateClient("health-probes", HttpVersion.Version20);
            _http1Client = CreateClient("health-probes-http1", HttpVersion.Version11);
        }

        public HttpClient CreateClient(string name) =>
            name == "health-probes-http1" ? _http1Client : _h2cClient;

        private HttpClient CreateClient(string name, Version version)
        {
            var client = new HttpClient(new RecordingHandler(name, Requests))
            {
                DefaultRequestVersion = version,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            return client;
        }
    }

    private sealed class RecordingHandler(
        string clientName,
        ConcurrentQueue<ProbeRequest> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Enqueue(new ProbeRequest(clientName, request.RequestUri!, request.Version));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"healthy\",\"checks\":[]}")
            });
        }
    }
}
