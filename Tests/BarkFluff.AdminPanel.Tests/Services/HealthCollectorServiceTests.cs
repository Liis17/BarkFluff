using Barkfluff.AdminPanel.Services;
using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        var runner = new StubDockerCommandRunner("", "{}");
        var collector = new HealthCollectorService(
            serviceProvider,
            new DockerService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<DockerService>.Instance,
                runner,
                new DockerContainerNameResolver(
                    runner,
                    NullLogger<DockerContainerNameResolver>.Instance,
                    "admin-panel")),
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

    [Fact]
    public async Task SeqDisabledKeepsServiceAndInfrastructureHealthAvailable()
    {
        var psOutput = string.Join('\n', new[]
        {
            DockerPsRow("barkfluff-cloud-messaging", "cloud-messaging"),
            DockerPsRow("barkfluff-postgres", "postgres"),
            DockerPsRow("barkfluff-redis", "redis"),
            DockerPsRow("barkfluff-rabbitmq", "rabbitmq")
        });
        var runner = new StubDockerCommandRunner(psOutput, OwnerLabels("barkfluff"));
        var docker = new DockerService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DockerService>.Instance,
            runner,
            new DockerContainerNameResolver(runner, NullLogger<DockerContainerNameResolver>.Instance, "panel-id"));
        var services = new ServiceCollection();
        services.AddSingleton(new SeqService(
            new HttpClient(),
            Options.Create(new SeqSettings { Enabled = false, ServerUrl = "" }),
            NullLogger<SeqService>.Instance));
        using var serviceProvider = services.BuildServiceProvider();
        var collector = new HealthCollectorService(
            serviceProvider,
            docker,
            new S3BrowserService(serviceProvider),
            new RecordingHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<HealthCollectorService>.Instance);

        var collectMethod = typeof(HealthCollectorService).GetMethod(
            "CollectAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(collectMethod);
        await (Task)collectMethod!.Invoke(collector, [CancellationToken.None])!;

        var snapshot = Assert.IsType<Barkfluff.AdminPanel.Models.Dtos.HealthSnapshot>(collector.GetSnapshot());
        var worker = Assert.Single(snapshot.Services, service => service.Name == "BarkFluff.CloudMessaging");
        Assert.Equal("cloud-messaging", worker.Container);
        Assert.Equal("running", worker.DockerState);
        Assert.Equal("healthy", worker.Status);
        Assert.Equal("healthy", Assert.Single(snapshot.Infrastructure, item => item.Name == "PostgreSQL").Status);
        Assert.Equal("healthy", Assert.Single(snapshot.Infrastructure, item => item.Name == "Redis").Status);
        Assert.Equal("healthy", Assert.Single(snapshot.Infrastructure, item => item.Name == "RabbitMQ").Status);
    }

    private static string OwnerLabels(string project) =>
        $$"""{"com.docker.compose.project":"{{project}}","com.docker.compose.service":"admin-panel"}""";

    private static string DockerPsRow(string name, string service) =>
        $$"""{"ID":"id-{{service}}","Names":"{{name}}","Image":"example:latest","State":"running","Status":"Up","Ports":"","Labels":"com.docker.compose.project=barkfluff,com.docker.compose.service={{service}}"}""";

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

    private sealed class StubDockerCommandRunner(
        string psOutput,
        string ownerLabels) : IDockerCommandRunner
    {
        public Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.SequenceEqual(["ps", "--all", "--format", "json"]))
                return Task.FromResult(psOutput);
            if (arguments.Length >= 4 && arguments[0] == "inspect" && arguments[2] == "{{json .Config.Labels}}")
                return Task.FromResult(ownerLabels);
            return Task.FromResult("");
        }
    }
}
