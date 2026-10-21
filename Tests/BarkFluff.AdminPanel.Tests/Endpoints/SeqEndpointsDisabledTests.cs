using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Net;
using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Endpoints;

public class SeqEndpointsDisabledTests
{
    [Fact]
    public async Task LogsReturnUnavailableWhileServiceListStillUsesDocker()
    {
        var runner = new StubDockerCommandRunner();
        var resolver = new DockerContainerNameResolver(
            runner,
            NullLogger<DockerContainerNameResolver>.Instance,
            "panel-id");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<SeqService>(_ => new SeqService(
            new HttpClient(new ThrowingHandler()),
            Options.Create(new SeqSettings { Enabled = false, ServerUrl = "" }),
            NullLogger<SeqService>.Instance));
        builder.Services.AddSingleton(new DockerService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DockerService>.Instance,
            runner,
            resolver));
        builder.Services.AddSingleton(new DockerRegistryService(
            new HttpClient(new ThrowingHandler()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DockerRegistryService>.Instance));
        builder.Services.AddSingleton<MetricsCacheDbContext>(_ => null!);

        await using var app = builder.Build();
        app.MapSeqEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var unavailable = await client.GetAsync("/api/seq/events");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var unavailableBody = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        Assert.Equal("Seq отключён", unavailableBody.RootElement.GetProperty("message").GetString());

        using var services = await client.GetAsync("/api/seq/services/status");
        Assert.Equal(HttpStatusCode.OK, services.StatusCode);
        using var servicesBody = JsonDocument.Parse(await services.Content.ReadAsStringAsync());
        var postgres = servicesBody.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "PostgreSQL");
        Assert.Equal("postgres", postgres.GetProperty("container").GetString());
    }

    private sealed class StubDockerCommandRunner : IDockerCommandRunner
    {
        private const string PsOutput = """
            {"ID":"id-postgres","Names":"barkfluff-postgres","Image":"example:latest","State":"running","Status":"Up","Ports":"","Labels":"com.docker.compose.project=barkfluff,com.docker.compose.service=postgres"}
            {"ID":"id-admin","Names":"barkfluff-admin-panel","Image":"example:latest","State":"running","Status":"Up","Ports":"","Labels":"com.docker.compose.project=barkfluff,com.docker.compose.service=admin-panel"}
            """;

        public List<string[]> Commands { get; } = [];
        public Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments);
            if (arguments.SequenceEqual(["ps", "--all", "--format", "json"]))
                return Task.FromResult(PsOutput);
            if (arguments.Length >= 4 && arguments[0] == "inspect" && arguments[2] == "{{json .Config.Labels}}")
                return Task.FromResult("{\"com.docker.compose.project\":\"barkfluff\",\"com.docker.compose.service\":\"admin-panel\"}");
            return Task.FromResult("");
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call: {request.RequestUri}");
    }
}
