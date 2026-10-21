using Barkfluff.AdminPanel;
using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Endpoints;

[Collection("LiteDb")]
public sealed class DockerEndpointsTests : IDisposable
{
    private const long OwnerId = 100;

    private const string ComposeYaml = """
        services:
          messages:
            image: docker.barkfluff.com/barkfluff-messages-nightly:latest
          admin-panel:
            image: docker.barkfluff.com/barkfluff-admin-panel-nightly:latest
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adminpanel-docker-endpoints-{Guid.NewGuid():N}");
    private readonly TokenDbContext _tokenDb;
    private readonly AuditDbContext _auditDb;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly StepUpService _stepUpService = new();
    private readonly AuthToken _requestToken = new()
    {
        Id = Guid.NewGuid(),
        AdminUsername = "alice",
        ApprovedByTelegramUserId = OwnerId
    };
    private readonly RecordingDockerCommandRunner _runner;
    private readonly DockerService _dockerService;
    private readonly DeployJobService _deployJobs;
    private readonly HttpClient _registryHttpClient;
    private readonly HttpClient _seqHttpClient;
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public DockerEndpointsTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), ComposeYaml);

        _tokenDb = new TokenDbContext(Options.Create(new LiteDbSettings
        {
            Path = Path.Combine(_directory, "tokens.db")
        }));
        _auditDb = new AuditDbContext(Options.Create(new AuditDbSettings
        {
            Path = Path.Combine(_directory, "audit.db")
        }));

        _runner = new RecordingDockerCommandRunner(CreateContainerList(
            ("barkfluff-settings", "settings", "docker.barkfluff.com/barkfluff-settings-nightly:latest"),
            ("barkfluff-postgres", "postgres", "postgres:16"),
            ("barkfluff-messages", "messages", "docker.barkfluff.com/barkfluff-messages-nightly:latest"),
            ("barkfluff-admin-panel", "admin-panel", "docker.barkfluff.com/barkfluff-admin-panel-nightly:latest")));
        var resolver = new DockerContainerNameResolver(
            _runner,
            NullLogger<DockerContainerNameResolver>.Instance,
            "panel-id");
        _dockerService = new DockerService(
            _cache,
            NullLogger<DockerService>.Instance,
            _runner,
            resolver);

        _registryHttpClient = new HttpClient(new AlwaysOkHandler())
        {
            BaseAddress = new Uri("https://registry.test")
        };
        _seqHttpClient = new HttpClient(new UnexpectedHttpHandler());

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Docker:ComposeFile"] = Path.Combine(_directory, "docker-compose.yml");
        builder.Configuration["Docker:ComposeBackupDirectory"] = Path.Combine(_directory, "compose-backups");
        builder.Services.AddSingleton(_tokenDb);
        builder.Services.AddSingleton(_auditDb);
        builder.Services.Configure<TelegramSettings>(settings =>
        {
            settings.ParsedAdmins = [new AdminUser(OwnerId, "alice")];
        });
        builder.Services.AddSingleton<AdminService>();
        builder.Services.AddSingleton<AuditService>();
        builder.Services.AddSingleton(_stepUpService);
        builder.Services.AddSingleton(_dockerService);
        builder.Services.AddSingleton<ComposeImageService>();
        builder.Services.AddSingleton<DockerRegistryService>(_ => new DockerRegistryService(
            _registryHttpClient,
            _cache,
            NullLogger<DockerRegistryService>.Instance));
        builder.Services.AddSingleton<SeqService>(_ => new SeqService(
            _seqHttpClient,
            Options.Create(new SeqSettings { Enabled = false, ServerUrl = "" }),
            NullLogger<SeqService>.Instance));
        builder.Services.AddSingleton<DeployJobService>();

        _app = builder.Build();
        _app.Use(next => async context =>
        {
            context.Items["AuthToken"] = _requestToken;
            await next(context);
        });
        _app.MapDockerEndpoints();

        _app.Services.GetRequiredService<AdminService>().EnsureBootstrapped();
        _deployJobs = _app.Services.GetRequiredService<DeployJobService>();

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stepUpService.Dispose();
        _registryHttpClient.Dispose();
        _seqHttpClient.Dispose();
        _cache.Dispose();
        _tokenDb.Dispose();
        _auditDb.Dispose();

        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task UpdateMany_MapsPrefixedAndLegacyContainerNamesToComposeServices()
    {
        using var response = await _client.PostAsync(
            "/api/docker/containers/update-many",
            new StringContent(
                "{\"containers\":[\"barkfluff-settings\",\"postgres_barkfluff\",\"admin-panel\"]}",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var job = Assert.Single(_deployJobs.GetRecentJobs());
        Assert.Equal(DeployJobKind.Update, job.Kind);
        Assert.Equal(new[] { "settings", "postgres", "admin-panel" }, job.Steps.Select(step => step.Service));
    }

    [Fact]
    public async Task BranchSwitch_MapsPrefixedAndLegacyNamesToComposeServiceKeys()
    {
        using var messagesResponse = await PostBranchSwitchAsync("barkfluff-messages", "dev");
        using var adminPanelResponse = await PostBranchSwitchAsync("admin-panel", "dev");

        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminPanelResponse.StatusCode);

        var jobs = _deployJobs.GetRecentJobs();
        Assert.Contains(jobs, job =>
            job.Kind == DeployJobKind.SwitchBranch &&
            Assert.Single(job.Steps).Service == "messages" &&
            job.Steps[0].Branch == "dev");
        Assert.Contains(jobs, job =>
            job.Kind == DeployJobKind.SwitchBranch &&
            Assert.Single(job.Steps).Service == "admin-panel" &&
            job.Steps[0].Branch == "dev");

        Assert.Equal("barkfluff-messages", (await _dockerService.GetContainerStatusAsync("barkfluff-messages"))?.Name);
        Assert.Equal("barkfluff-admin-panel", (await _dockerService.GetContainerStatusAsync("admin-panel"))?.Name);
    }

    private async Task<HttpResponseMessage> PostBranchSwitchAsync(string container, string branch)
    {
        var request = _stepUpService.CreateRequest(new PendingStepUp
        {
            ActionKey = StepUpActions.DockerBranch,
            Params = $"container={container}",
            TokenId = _requestToken.Id,
            TargetTelegramUserId = OwnerId
        });
        Assert.True(_stepUpService.Resolve(request.ConfirmationId, StepUpStatus.Approved, OwnerId));

        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/docker/containers/{container}/branch")
        {
            Content = new StringContent($"{{\"branch\":\"{branch}\"}}", Encoding.UTF8, "application/json")
        };
        message.Headers.Add(RequireStepUpFilter.HeaderName, request.ConfirmationId);
        return await _client.SendAsync(message);
    }

    private static string CreateContainerList(params (string Name, string Service, string Image)[] containers) =>
        string.Join('\n', containers.Select((container, index) => JsonSerializer.Serialize(new
        {
            ID = $"id-{index}",
            Names = container.Name,
            Image = container.Image,
            State = "running",
            Status = "Up",
            Ports = "",
            Labels = $"com.docker.compose.project=barkfluff,com.docker.compose.service={container.Service}"
        })));

    private sealed class RecordingDockerCommandRunner(string psOutput) : IDockerCommandRunner
    {
        public List<string[]> Commands { get; } = [];

        public Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments);

            if (arguments.SequenceEqual(["ps", "--all", "--format", "json"]))
                return Task.FromResult(psOutput);
            if (arguments.Length >= 4 && arguments[0] == "inspect" && arguments[2] == "{{json .Config.Labels}}")
                return Task.FromResult("{\"com.docker.compose.project\":\"barkfluff\",\"com.docker.compose.service\":\"admin-panel\"}");

            return Task.FromResult("");
        }
    }

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call: {request.RequestUri}");
    }
}
