using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

[Collection("LiteDb")]
public class DeployJobServiceTests
{
    private const string MessagesImage = "docker.barkfluff.com/barkfluff-messages-nightly:latest";

    [Fact]
    public void BulkDeploy_IncludesAllPlatformServicesAndExcludesInfrastructure()
    {
        Assert.Equal(
            PlatformServiceRegistry.BarkFluff.Select(s => s.Container).OrderBy(s => s),
            DeployJobService.DeployOrder.OrderBy(s => s));
        Assert.Equal("settings", DeployJobService.DeployOrder[0]);
    }

    [Fact]
    public async Task Update_UsesComposeServiceAndResolvedContainerName()
    {
        using var history = new TempHistory();
        var runner = CreateOperationalRunner("running|healthy");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);
        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), history, jobs => jobs.EnqueueUpdate(["messages"]));

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
        using var history = new TempHistory();
        var runner = CreateOperationalRunner("exited|none");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);
        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), history, jobs => jobs.EnqueueUpdate(["messages"]));

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
        using var history = new TempHistory();
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
            var job = await RunJobAsync(runner, docker, compose, history, jobs => jobs.EnqueueBranchSwitch("messages", "dev"));

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

    [Fact]
    public async Task Update_FinishedJobWithRolledBackStep_KeepsReasonAndRollbackAfterPanelRestart()
    {
        using var history = new TempHistory();
        var runner = CreateOperationalRunner("exited|none");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);
        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), history, jobs => jobs.EnqueueUpdate(["messages"]));

        await RestartPanelAsync(history, CreateOperationalRunner("running|healthy"), service =>
        {
            var restored = Assert.Single(service.GetRecentJobs());
            Assert.Equal(job.Id, restored.Id);
            Assert.Equal(DeployJobKind.Update, restored.Kind);
            Assert.Equal(DeployJobState.Failed, restored.State);
            Assert.NotNull(restored.FinishedAtUtc);
            Assert.Equal(DateTimeKind.Utc, restored.CreatedAtUtc.Kind);

            var step = Assert.Single(restored.Steps);
            Assert.Equal(DeployStepState.Failed, step.State);
            Assert.True(step.RolledBack);
            Assert.Equal(job.Steps[0].Message, step.Message);
            Assert.False(string.IsNullOrWhiteSpace(step.Message));
        });
    }

    [Fact]
    public async Task Start_RestoresFailedStepReasonAndRollbackStateFromHistory()
    {
        using var history = new TempHistory();
        var jobId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        const string reason = "Контейнер не запустился (state=exited). Откачено на предыдущий образ";
        SeedHistory(history, new DeployJobRecord
        {
            Id = jobId,
            Kind = "Update",
            State = "Failed",
            Error = $"messages: {reason}",
            CreatedAtUtc = createdAt,
            StartedAtUtc = createdAt.AddSeconds(2),
            FinishedAtUtc = createdAt.AddMinutes(3),
            Steps =
            [
                Step("settings", "Completed", "Обновлён, health-check пройден"),
                Step("messages", "Failed", reason, rolledBack: true),
                Step("files", "Failed", "Не удалось проверить состояние контейнера", rolledBack: false)
            ]
        });

        await RestartPanelAsync(history, CreateOperationalRunner("running|healthy"), service =>
        {
            var job = Assert.Single(service.GetRecentJobs());
            Assert.Equal(jobId, job.Id);
            Assert.Equal(DeployJobState.Failed, job.State);
            Assert.Equal(createdAt, job.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, job.CreatedAtUtc.Kind);

            var failed = job.Steps.Where(s => s.State == DeployStepState.Failed).ToList();
            Assert.Equal(["messages", "files"], failed.Select(s => s.Service));
            Assert.Equal(reason, failed[0].Message);
            Assert.True(failed[0].RolledBack);
            Assert.False(failed[1].RolledBack);
        });
    }

    [Fact]
    public async Task Start_MarksUnfinishedJobsInterruptedAndDoesNotRunThemAgain()
    {
        using var history = new TempHistory();
        var createdAt = DateTime.UtcNow.AddMinutes(-10);
        var runningId = Guid.NewGuid();
        var queuedId = Guid.NewGuid();
        SeedHistory(history,
            new DeployJobRecord
            {
                Id = runningId,
                Kind = "Update",
                State = "Running",
                CreatedAtUtc = createdAt,
                StartedAtUtc = createdAt.AddSeconds(1),
                Steps =
                [
                    Step("settings", "Completed", "Обновлён, health-check пройден"),
                    Step("beacon", "InProgress"),
                    Step("files", "Pending")
                ]
            },
            new DeployJobRecord
            {
                Id = queuedId,
                Kind = "Restart",
                State = "Queued",
                CreatedAtUtc = createdAt.AddMinutes(1),
                Steps = [Step("messages", "Pending")]
            });

        var runner = CreateOperationalRunner("running|healthy");
        await RestartPanelAsync(history, runner, service =>
        {
            var running = service.GetJob(runningId)!;
            Assert.Equal(DeployJobState.Failed, running.State);
            Assert.Equal(InterruptedReason, running.Error);
            Assert.NotNull(running.FinishedAtUtc);
            Assert.Equal(
                [DeployStepState.Completed, DeployStepState.Failed, DeployStepState.Failed],
                running.Steps.Select(s => s.State));
            Assert.Equal("Обновлён, health-check пройден", running.Steps[0].Message);
            Assert.All(running.Steps.Skip(1), step => Assert.Equal(InterruptedReason, step.Message));

            var queued = service.GetJob(queuedId)!;
            Assert.Equal(DeployJobState.Failed, queued.State);
            Assert.Equal(InterruptedReason, queued.Error);
            Assert.All(queued.Steps, step => Assert.Equal(DeployStepState.Failed, step.State));
        });

        // Прерванные задачи не ставятся в очередь повторно: ни одной docker-команды не выполнено
        Assert.Empty(runner.Commands);

        var stored = ReadHistory(history);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, record => Assert.Equal("Failed", record.State));
    }

    [Fact]
    public async Task Update_WhenHistoryFileCannotBeOpened_StillCompletesJob()
    {
        // Каталог истории не создать: путь проходит через обычный файл
        using var blocker = new TempHistory();
        File.WriteAllText(blocker.Path, "not a directory");
        using var history = new TempHistory(Path.Combine(blocker.Path, "deploy_jobs.db"));
        var runner = CreateOperationalRunner("running|healthy");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var docker = CreateDockerService(runner, cache);

        var job = await RunJobAsync(runner, docker, CreateComposeImageService(), history, jobs => jobs.EnqueueUpdate(["messages"]));

        Assert.Equal(DeployJobState.Completed, job.State);
        Assert.Equal(DeployStepState.Completed, Assert.Single(job.Steps).State);
    }

    [Fact]
    public async Task Start_KeepsOnlyTwentyNewestFinishedJobsInHistory()
    {
        using var history = new TempHistory();
        var baseTime = DateTime.UtcNow.AddDays(-1);
        var records = Enumerable.Range(0, 22).Select(i => new DeployJobRecord
        {
            Id = Guid.NewGuid(),
            Kind = "Restart",
            State = "Completed",
            CreatedAtUtc = baseTime.AddMinutes(i),
            FinishedAtUtc = baseTime.AddMinutes(i),
            Steps = []
        }).ToArray();
        SeedHistory(history, records);

        await RestartPanelAsync(history, CreateOperationalRunner("running|healthy"), service =>
        {
            Assert.Equal(20, service.GetRecentJobs().Count);
        });

        var stored = ReadHistory(history);
        // Сравнение по Id: LiteDB хранит время с точностью до миллисекунд, поэтому сравнение по времени нестабильно
        Assert.Equal(
            records.Skip(2).Select(record => record.Id).OrderBy(id => id),
            stored.Select(record => record.Id).OrderBy(id => id));
    }

    private const string InterruptedReason = "Прервано перезапуском панели";

    private static readonly HttpClient SeqHttpClient = new(new UnexpectedHttpHandler());

    private static DeployJobService CreateDeployJobService(
        DockerService docker,
        ComposeImageService compose,
        DeployJobDbContext history) =>
        new(
            docker,
            compose,
            new SeqService(
                SeqHttpClient,
                Options.Create(new SeqSettings { Enabled = false, ServerUrl = "" }),
                NullLogger<SeqService>.Instance),
            NullLogger<DeployJobService>.Instance,
            history);

    /// <summary>Запустить панель поверх истории, остановить её и проверить состояние уже после остановки</summary>
    private static async Task RestartPanelAsync(
        TempHistory history,
        RecordingDockerCommandRunner runner,
        Action<DeployJobService> check)
    {
        using var historyDb = new DeployJobDbContext(history.Path);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateDeployJobService(CreateDockerService(runner, cache), CreateComposeImageService(), historyDb);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await service.StopAsync(CancellationToken.None);
            check(service);
        }
        finally
        {
            service.Dispose();
        }
    }

    private static void SeedHistory(TempHistory history, params DeployJobRecord[] records)
    {
        using var db = new DeployJobDbContext(history.Path);
        foreach (var record in records)
            db.Jobs.Upsert(record);
    }

    private static List<DeployJobRecord> ReadHistory(TempHistory history)
    {
        using var db = new DeployJobDbContext(history.Path);
        return db.Jobs.FindAll().ToList();
    }

    private static DeployStepRecord Step(string service, string state, string? message = null, bool rolledBack = false) => new()
    {
        Service = service,
        State = state,
        Message = message,
        RolledBack = rolledBack
    };

    private static async Task<DeployJob> RunJobAsync(
        RecordingDockerCommandRunner runner,
        DockerService docker,
        ComposeImageService compose,
        TempHistory history,
        Func<DeployJobService, DeployJob> enqueue)
    {
        using var historyDb = new DeployJobDbContext(history.Path);
        var service = CreateDeployJobService(docker, compose, historyDb);
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
                if (template.Contains("/docker-compose.yml", StringComparison.Ordinal)) return "/node/docker-compose.yml";
                if (template.Contains("/.env", StringComparison.Ordinal)) return "/node/.env";
            }
            if (template.Contains("project.config_files", StringComparison.Ordinal))
                return "/node/docker-compose.yml,/node/override.yml";
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

    private sealed class TempHistory(string? path = null) : IDisposable
    {
        public string Path { get; } = path ?? System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"adminpanel-deploy-history-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            foreach (var file in new[] { Path, Path + "-log" })
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call: {request.RequestUri}");
    }
}
