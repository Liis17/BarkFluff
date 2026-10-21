using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class DockerContainerNameResolverTests
{
    [Fact]
    public void ParseComposeLabels_readsCommaSeparatedKeyValuePairs()
    {
        var labels = DockerContainerNameResolver.ParseComposeLabels(
            "com.docker.compose.project=barkfluff,com.docker.compose.service=postgres,custom=value=with-equals");

        Assert.Equal("barkfluff", labels["com.docker.compose.project"]);
        Assert.Equal("postgres", labels["com.docker.compose.service"]);
        Assert.Equal("value=with-equals", labels["custom"]);

        var container = Assert.Single(DockerContainerNameResolver.ParseDockerPsOutput(
            "{\"ID\":\"id-1\",\"Names\":\"barkfluff-postgres\",\"Image\":\"example:latest\",\"State\":\"running\",\"Labels\":\"com.docker.compose.project=barkfluff,com.docker.compose.service=postgres\"}"));
        Assert.Equal("barkfluff", container.ComposeProject);
        Assert.Equal("postgres", container.ComposeService);
    }

    [Fact]
    public async Task ResolvesLegacyPostgresAndPrefixedComposeNames()
    {
        var legacyRunner = new RecordingDockerCommandRunner("", OwnerLabels("barkfluff"));
        var legacyResolver = CreateResolver(legacyRunner);

        Assert.Equal("postgres", await legacyResolver.ResolveServiceNameAsync("postgres_barkfluff"));
        Assert.Equal("postgres_barkfluff", await legacyResolver.ResolveContainerNameAsync("postgres"));

        var runner = new RecordingDockerCommandRunner(ComposeContainers(
            ("barkfluff-postgres", "postgres", "barkfluff"),
            ("barkfluff-admin-panel", "admin-panel", "barkfluff")), OwnerLabels("barkfluff"));
        var resolver = CreateResolver(runner);

        Assert.Equal("barkfluff-postgres", await resolver.ResolveContainerNameAsync("postgres"));
        Assert.Equal("postgres", await resolver.ResolveServiceNameAsync("barkfluff-postgres"));
        Assert.Equal("barkfluff-admin-panel", await resolver.ResolveContainerNameAsync("admin-panel"));
    }

    [Fact]
    public async Task DoesNotResolveContainerFromAnotherComposeProject()
    {
        var runner = new RecordingDockerCommandRunner(ComposeContainers(
            ("other-postgres", "postgres", "another-project"),
            ("barkfluff-admin-panel", "admin-panel", "barkfluff")), OwnerLabels("barkfluff"));
        var resolver = CreateResolver(runner);

        Assert.Null(await resolver.TryResolveComposeServiceNameAsync("other-postgres"));
        Assert.Equal("other-postgres", await resolver.ResolveServiceNameAsync("other-postgres"));
        Assert.Equal("postgres_barkfluff", await resolver.ResolveContainerNameAsync("postgres"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveContainerNameAsync("other-postgres"));
    }

    [Fact]
    public async Task InvalidateReloadsContainerNamesAfterRecreation()
    {
        var runner = new RecordingDockerCommandRunner(
            ComposeContainers(("barkfluff-postgres-old", "postgres", "barkfluff")),
            OwnerLabels("barkfluff"));
        var resolver = CreateResolver(runner);

        Assert.Equal("barkfluff-postgres-old", await resolver.ResolveContainerNameAsync("postgres"));
        runner.PsOutput = ComposeContainers(("barkfluff-postgres-new", "postgres", "barkfluff"));
        resolver.Invalidate();

        Assert.Equal("barkfluff-postgres-new", await resolver.ResolveContainerNameAsync("postgres"));
    }

    [Fact]
    public async Task CancellationIsPassedToContainerDiscovery()
    {
        var runner = new RecordingDockerCommandRunner("", OwnerLabels("barkfluff"));
        var resolver = CreateResolver(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveContainerNameAsync("postgres", cancellation.Token));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task DeployAndRollbackDockerOperationsUseResolvedContainerAndComposeServiceNames()
    {
        var runner = CreateOperationalRunner();
        var docker = CreateDockerService(runner);

        var restart = await docker.RestartContainerAsync("postgres");
        Assert.True(restart.Success);
        Assert.Contains(runner.Commands, command => command.SequenceEqual(["restart", "-t", "30", "barkfluff-postgres"]));

        await docker.ComposePullAsync("postgres");
        await docker.ComposeUpAsync("postgres");

        var composeRuns = runner.Commands.Where(command => command[0] == "run").ToArray();
        Assert.Contains(composeRuns, command => command[^2] == "pull" && command[^1] == "postgres");
        Assert.Contains(composeRuns, command => command.TakeLast(6).SequenceEqual(
            ["up", "--force-recreate", "--no-deps", "--build", "-d", "postgres"]));
    }

    [Fact]
    public async Task SelfRestartAndUpdateResolvePanelContainerButKeepComposeServiceKey()
    {
        var runner = CreateOperationalRunner();
        var docker = CreateDockerService(runner);

        Assert.True((await docker.RestartAdminPanelAsync()).Success);
        var restartCommand = Assert.Single(runner.Commands, command => command[0] == "run");
        Assert.Contains("sleep 2 && docker restart barkfluff-admin-panel", restartCommand[^1], StringComparison.Ordinal);

        runner.Commands.Clear();
        Assert.True((await docker.UpdateAdminPanelAsync()).Success);
        var updateCommand = Assert.Single(runner.Commands, command => command[0] == "run");
        Assert.Contains("'pull' 'admin-panel'", updateCommand[^1], StringComparison.Ordinal);
        Assert.Contains("'-f' 'C:\\node\\docker-compose.yml'", updateCommand[^1], StringComparison.Ordinal);
        Assert.Contains(runner.Commands, command => command[0] == "inspect" && command[^1] == "barkfluff-admin-panel");
    }

    private static DockerContainerNameResolver CreateResolver(RecordingDockerCommandRunner runner) =>
        new(runner, NullLogger<DockerContainerNameResolver>.Instance, "panel-id");

    private static DockerService CreateDockerService(RecordingDockerCommandRunner runner) =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<DockerService>.Instance, runner, CreateResolver(runner));

    private static RecordingDockerCommandRunner CreateOperationalRunner() => new(
        ComposeContainers(
            ("barkfluff-postgres", "postgres", "barkfluff"),
            ("barkfluff-admin-panel", "admin-panel", "barkfluff")),
        OwnerLabels("barkfluff"),
        args =>
        {
            if (args[0] != "inspect" || args.Length < 3)
                return "";

            var template = args[2];
            if (template == "{{.Config.Image}}")
                return "admin-panel-image";
            if (template.Contains(".Mounts", StringComparison.Ordinal))
            {
                if (template.Contains("/var/run/docker.sock", StringComparison.Ordinal)) return "/var/run/docker.sock";
                if (template.Contains("/docker-compose.yml", StringComparison.Ordinal)) return "C:\\node\\docker-compose.yml";
                if (template.Contains("/.env", StringComparison.Ordinal)) return "C:\\node\\.env";
            }
            if (template.Contains("project.config_files", StringComparison.Ordinal))
                return "C:\\node\\docker-compose.yml,C:\\node\\override.yml";
            if (template.Contains(".State.Status", StringComparison.Ordinal))
                return "running|healthy";
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
        public string PsOutput { get; set; } = psOutput;
        public List<string[]> Commands { get; } = [];

        public Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments);

            if (arguments.SequenceEqual(["ps", "--all", "--format", "json"]))
                return Task.FromResult(PsOutput);
            if (arguments.Length >= 4 && arguments[0] == "inspect" && arguments[2] == "{{json .Config.Labels}}")
                return Task.FromResult(ownerLabels);

            return Task.FromResult(output?.Invoke(arguments) ?? "");
        }
    }
}
