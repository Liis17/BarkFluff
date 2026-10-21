using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class DeployJobServiceTests
{
    private const string MessagesImage = "docker.barkfluff.com/barkfluff-messages-nightly:latest";

    [Fact]
    public async Task Update_UsesComposeServiceAndResolvedContainerName()
    {
        var runner = CreateOperationalRunner("running|healthy");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);
        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), jobs => jobs.EnqueueUpdate(["messages"]));

        Assert.Equal(DeployJobState.Completed, job.State);
        Assert.Equal(DeployStepState.Completed, Assert.Single(job.Steps).State);

        Assert.Contains(runner.Commands, command =>
            command[0] == "inspect" && command[2] == "{{.Image}}" && command[^1] == "barkfluff-messages");
        Assert.Contains(runner.Commands, command =>
            command[0] == "inspect" && command[2].Contains(".State.Status", StringComparison.Ordinal) && command[^1] == "barkfluff-messages");

        var composeRuns = runner.Commands.Where(command => command[0] == "run").ToArray();
        Assert.Contains(composeRuns, command => command[^2] == "pull" && command[^1] == "messages");
        Assert.Contains(composeRuns, command => command.TakeLast(6).SequenceEqual(
            ["up", "--force-recreate", "--no-deps", "--build", "-d", "messages"]));
    }

    [Fact]
    public async Task Update_WhenReplacementFails_RollsBackUsingComposeServiceAndResolvedContainer()
    {
        var runner = CreateOperationalRunner("exited|none");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);
        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), jobs => jobs.EnqueueUpdate(["messages"]));

        Assert.Equal(DeployJobState.Failed, job.State);
        Assert.True(Assert.Single(job.Steps).RolledBack);
        Assert.Contains(runner.Commands, command =>
            command[0] == "inspect" && command[2].Contains(".State.Status", StringComparison.Ordinal) && command[^1] == "barkfluff-messages");
        Assert.Contains(runner.Commands, command => command.SequenceEqual(["tag", "sha256:old", MessagesImage]));

        var composeRuns = runner.Commands.Where(command => command[0] == "run").ToArray();
        Assert.Single(composeRuns, command => command[^2] == "pull" && command[^1] == "messages");
        Assert.Equal(2, composeRuns.Count(command => command.TakeLast(6).SequenceEqual(
            ["up", "--force-recreate", "--no-deps", "--build", "-d", "messages"])));
    }

    [Fact]
    public async Task BranchSwitch_WhenReplacementFails_RestoresComposeAndRecreatesComposeService()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"adminpanel-deploy-job-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var composePath = Path.Combine(directory, "docker-compose.yml");
        var originalCompose = $"services:\n  messages:\n    image: {MessagesImage}\n";
        await File.WriteAllTextAsync(composePath, originalCompose);

        try
        {
            var runner = CreateOperationalRunner("exited|none");
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var docker = CreateDockerService(runner, cache);
            var compose = CreateComposeImageService(composePath, Path.Combine(directory, "backups"));
            var job = await RunJobAsync(runner, docker, compose, jobs => jobs.EnqueueBranchSwitch("messages", "dev"));

            Assert.Equal(DeployJobState.Failed, job.State);
            Assert.True(Assert.Single(job.Steps).RolledBack);
            Assert.Equal(originalCompose, await File.ReadAllTextAsync(composePath));
            Assert.Contains(runner.Commands, command =>
                command[0] == "inspect" && command[2].Contains(".State.Status", StringComparison.Ordinal) && command[^1] == "barkfluff-messages");

            var composeRuns = runner.Commands.Where(command => command[0] == "run").ToArray();
            Assert.Single(composeRuns, command => command[^2] == "pull" && command[^1] == "messages");
            Assert.Equal(2, composeRuns.Count(command => command.TakeLast(6).SequenceEqual(
                ["up", "--force-recreate", "--no-deps", "--build", "-d", "messages"])));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<DeployJob> RunJobAsync(
        RecordingDockerCommandRunner runner,
        DockerService docker,
        ComposeImageService compose,
        Func<DeployJobService, DeployJob> enqueue)
    {
        using var seqHttpClient = new HttpClient(new UnexpectedHttpHandler());
        var service = new DeployJobService(
            docker,
            compose,
            new SeqService(
                seqHttpClient,
                Options.Create(new SeqSettings { Enabled = false, ServerUrl = "" }),
                NullLogger<SeqService>.Instance),
            NullLogger<DeployJobService>.Instance);
        await service.StartAsync(CancellationToken.None);

        try
        {
            var job = enqueue(service);
            await runner.PruneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await service.StopAsync(CancellationToken.None);
            return job;
        }
        catch
        {
            await service.StopAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            service.Dispose();
        }
    }

    private static DockerService CreateDockerService(RecordingDockerCommandRunner runner, IMemoryCache cache) =>
        new(
            cache,
            NullLogger<DockerService>.Instance,
            runner,
            new DockerContainerNameResolver(runner, NullLogger<DockerContainerNameResolver>.Instance, "panel-id"));

    private static ComposeImageService CreateComposeImageService(string? composePath = null, string? backupPath = null)
    {
        var settings = new Dictionary<string, string?>();
        if (composePath is not null)
            settings["Docker:ComposeFile"] = composePath;
        if (backupPath is not null)
            settings["Docker:ComposeBackupDirectory"] = backupPath;

        return new ComposeImageService(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<ComposeImageService>.Instance);
    }

    private static RecordingDockerCommandRunner CreateOperationalRunner(string healthOutput) => new(
        ComposeContainers(
            ("barkfluff-messages", "messages", "barkfluff"),
            ("barkfluff-admin-panel", "admin-panel", "barkfluff")),
        OwnerLabels("barkfluff"),
        args =>
        {
            if (args[0] != "inspect" || args.Length < 3)
                return "";

            var template = args[2];
            if (template == "{{.Image}}")
                return "sha256:old";
            if (template == "{{.Config.Image}}")
                return args[^1] == "barkfluff-messages" ? MessagesImage : "admin-panel-image";
            if (template.Contains(".Mounts", StringComparison.Ordinal))
            {
                if (template.Contains("/var/run/docker.sock", StringComparison.Ordinal)) return "/var/run/docker.sock";
                if (template.Contains("/docker-compose.yml", StringComparison.Ordinal)) return "C:\\node\\docker-compose.yml";
                if (template.Contains("/.env", StringComparison.Ordinal)) return "C:\\node\\.env";
            }
            if (template.Contains("project.config_files", StringComparison.Ordinal))
                return "C:\\node\\docker-compose.yml,C:\\node\\override.yml";
            if (template.Contains(".State.Status", StringComparison.Ordinal))
                return healthOutput;
            return "";
        });

    private static string OwnerLabels(string project) =>
        $$"""{"com.docker.compose.project":"{{project}}","com.docker.compose.service":"admin-panel"}""";

    private static string ComposeContainers(params (string Name, string Service, string Project)[] containers) =>
        string.Join('\n', containers.Select((container, index) =>
            $$"""{"ID":"id-{{index}}","Names":"{{container.Name}}","Image":"example:latest","State":"running","Status":"Up","Ports":"","Labels":"com.docker.compose.project={{container.Project}},com.docker.compose.service={{container.Service}}"}"""));

    private sealed class RecordingDockerCommandRunner(
        string psOutput,
        string ownerLabels,
        Func<string[], string>? output = null) : IDockerCommandRunner
    {
        public List<string[]> Commands { get; } = [];
        public TaskCompletionSource<bool> PruneCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments);

            if (arguments.SequenceEqual(["ps", "--all", "--format", "json"]))
                return Task.FromResult(psOutput);
            if (arguments.Length >= 4 && arguments[0] == "inspect" && arguments[2] == "{{json .Config.Labels}}")
                return Task.FromResult(ownerLabels);
            if (arguments.SequenceEqual(["image", "prune", "-f"]))
                PruneCompleted.TrySetResult(true);

            return Task.FromResult(output?.Invoke(arguments) ?? "");
        }
    }

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call: {request.RequestUri}");
    }
}
