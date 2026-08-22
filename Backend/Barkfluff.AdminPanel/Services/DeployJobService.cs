using Barkfluff.AdminPanel.Models;

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Серверная очередь деплой-операций (обновление, перезапуск, переключение ветки).
/// Один потребитель: задачи выполняются последовательно, конкурентные docker compose
/// операции исключены. Каждый шаг завершается health-check'ом контейнера; при явном
/// падении (crash-loop / exited / unhealthy) шаг откатывается на предыдущий образ или ветку.
/// docker image prune выполняется только после завершения всей задачи — старые образы
/// должны оставаться доступными для отката.
/// </summary>
public class DeployJobService : BackgroundService
{
    /// <summary>
    /// Порядок обработки сервисов: configuration первым — от него зависят остальные.
    /// </summary>
    public static readonly string[] DeployOrder =
    [
        "configuration",
        "beacon",
        "files",
        "identity",
        "messages",
        "notification",
        "users",
        "fast-auth",
        "updates",
        "onliner",
        "web"
    ];

    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialSettleDelay = TimeSpan.FromSeconds(2);
    private const int MaxFinishedJobs = 20;

    private readonly Channel<DeployJob> _queue = Channel.CreateUnbounded<DeployJob>();
    private readonly ConcurrentDictionary<Guid, DeployJob> _jobs = new();
    private readonly DockerService _docker;
    private readonly ComposeImageService _compose;
    private readonly ILogger<DeployJobService> _logger;

    public DeployJobService(DockerService docker, ComposeImageService compose, ILogger<DeployJobService> logger)
    {
        _docker = docker;
        _compose = compose;
        _logger = logger;
    }

    public DeployJob EnqueueUpdate(IEnumerable<string> services) => Enqueue(DeployJobKind.Update, services, null);

    public DeployJob EnqueueRestart(IEnumerable<string> services) => Enqueue(DeployJobKind.Restart, services, null);

    public DeployJob EnqueueBranchSwitch(string service, string branch) => Enqueue(DeployJobKind.SwitchBranch, [service], branch);

    private DeployJob Enqueue(DeployJobKind kind, IEnumerable<string> services, string? branch)
    {
        var ordered = OrderServices(services);
        var job = new DeployJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Steps = ordered.Select(service => new DeployStep { Service = service, Branch = branch }).ToList(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _jobs[job.Id] = job;
        TrimFinishedJobs();
        _queue.Writer.TryWrite(job);

        _logger.LogInformation("Deploy job {JobId} ({Kind}) в очереди: {Services}", job.Id, kind, string.Join(", ", ordered));
        return job;
    }

    /// <summary>
    /// Отсортировать сервисы по <see cref="DeployOrder"/>; неизвестные очереди — в конце в исходном порядке
    /// </summary>
    private static IReadOnlyList<string> OrderServices(IEnumerable<string> services)
    {
        var list = services
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var known = DeployOrder.Where(s => list.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        var unknown = list.Where(s => !DeployOrder.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        known.AddRange(unknown);
        return known;
    }

    public DeployJob? GetJob(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    /// <summary>Недавние задачи: активные первыми, затем новые сверху</summary>
    public IReadOnlyList<DeployJob> GetRecentJobs() =>
        _jobs.Values
            .OrderByDescending(j => j.State is DeployJobState.Queued or DeployJobState.Running)
            .ThenByDescending(j => j.CreatedAtUtc)
            .ToList();

    private void TrimFinishedJobs()
    {
        var stale = _jobs.Values
            .Where(j => j.State is DeployJobState.Completed or DeployJobState.Failed)
            .OrderByDescending(j => j.FinishedAtUtc ?? j.CreatedAtUtc)
            .Skip(MaxFinishedJobs)
            .ToList();
        foreach (var job in stale)
            _jobs.TryRemove(job.Id, out _);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            await RunJobAsync(job, stoppingToken);
    }

    private async Task RunJobAsync(DeployJob job, CancellationToken ct)
    {
        job.State = DeployJobState.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        _logger.LogInformation("Deploy job {JobId} ({Kind}) запущен", job.Id, job.Kind);

        foreach (var step in job.Steps)
        {
            step.State = DeployStepState.InProgress;
            try
            {
                await RunStepAsync(job, step, ct);
                step.State = DeployStepState.Completed;
            }
            catch (OperationCanceledException)
            {
                step.State = DeployStepState.Failed;
                step.Message = "Остановка админ-панели";
                break;
            }
            catch (Exception ex)
            {
                step.State = DeployStepState.Failed;
                step.Message = ex.Message;
                _logger.LogError(ex, "Deploy job {JobId}: шаг {Service} провалился", job.Id, step.Service);
            }
        }

        // Очистка неиспользуемых образов — только после всей задачи:
        // старые образы нужны для отката на предыдущих шагах
        if (job.Kind is DeployJobKind.Update or DeployJobKind.SwitchBranch)
        {
            try
            {
                await _docker.PruneImagesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deploy job {JobId}: не удалось очистить неиспользуемые образы", job.Id);
            }
        }

        var failed = job.Steps.Where(s => s.State == DeployStepState.Failed).ToList();
        job.State = failed.Count > 0 ? DeployJobState.Failed : DeployJobState.Completed;
        job.Error = failed.Count > 0
            ? string.Join("; ", failed.Select(s => $"{s.Service}: {s.Message}"))
            : null;
        job.FinishedAtUtc = DateTime.UtcNow;
        _logger.LogInformation("Deploy job {JobId} завершён: {State}{Error}", job.Id, job.State, job.Error is null ? "" : $" — {job.Error}");
    }

    private async Task RunStepAsync(DeployJob job, DeployStep step, CancellationToken ct)
    {
        // Админ-панель нельзя пересоздать из собственного процесса — ей помогает helper-контейнер
        if (string.Equals(step.Service, "admin-panel", StringComparison.OrdinalIgnoreCase))
        {
            var result = job.Kind == DeployJobKind.Restart
                ? await _docker.RestartAdminPanelAsync()
                : await _docker.UpdateAdminPanelAsync();
            if (!result.Success)
                throw new InvalidOperationException(result.Message);

            step.Message = "Админ-панель обновляется через helper-контейнер";
            return;
        }

        switch (job.Kind)
        {
            case DeployJobKind.Restart:
                await RestartStepAsync(step, ct);
                return;
            case DeployJobKind.Update:
                await UpdateStepAsync(step, ct);
                return;
            case DeployJobKind.SwitchBranch:
                await BranchStepAsync(step, ct);
                return;
        }
    }

    /// <summary>Имя контейнера для docker inspect/restart (compose-команды используют имя сервиса)</summary>
    private static string ContainerOf(DeployStep step) => DockerService.ConvertServiceNameToContainerName(step.Service);

    private async Task RestartStepAsync(DeployStep step, CancellationToken ct)
    {
        var result = await _docker.RestartContainerAsync(ContainerOf(step));
        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        var health = await WaitHealthyAsync(ContainerOf(step), ct);
        if (!health.Ok)
            throw new InvalidOperationException(health.Reason);

        step.Message = "Перезапущен";
    }

    private async Task UpdateStepAsync(DeployStep step, CancellationToken ct)
    {
        var container = ContainerOf(step);

        // Запоминаем старый образ — он понадобится для отката, если новый окажется битым
        var oldImageId = await _docker.GetContainerImageIdAsync(container);
        var oldReference = oldImageId is null ? null : await _docker.GetContainerImageReferenceAsync(container);

        await _docker.ComposePullAsync(step.Service);
        await _docker.ComposeUpAsync(step.Service);

        var health = await WaitHealthyAsync(container, ct);
        if (health.Ok)
        {
            step.Message = "Обновлён, health-check пройден";
            return;
        }

        // Откат только при явном падении: контейнер вышел / crash-loop / unhealthy.
        // Таймаут — не доказательство поломки (сервис может просто медленно стартовать).
        if (health.DefiniteFailure && oldImageId is not null && oldReference is not null)
        {
            await _docker.TagImageAsync(oldImageId, oldReference);
            await _docker.ComposeUpAsync(step.Service);
            step.RolledBack = true;
            _logger.LogWarning("Сервис {Service} откачен на предыдущий образ {Reference}", step.Service, oldReference);
            throw new InvalidOperationException($"{health.Reason}. Откачено на предыдущий образ");
        }

        throw new InvalidOperationException(health.Reason);
    }

    private async Task BranchStepAsync(DeployStep step, CancellationToken ct)
    {
        var container = ContainerOf(step);
        var previousCompose = await _compose.SetBranchAsync(step.Service, step.Branch!);

        try
        {
            await _docker.ComposePullAsync(step.Service);
            await _docker.ComposeUpAsync(step.Service);
        }
        catch (Exception)
        {
            // Контейнер ещё не трогали — достаточно вернуть compose-файл
            await _compose.RestoreAsync(previousCompose);
            throw;
        }

        var health = await WaitHealthyAsync(container, ct);
        if (health.Ok)
        {
            step.Message = $"Ветка {step.Branch} применена";
            return;
        }

        // Возвращаем прежнюю ветку в compose; контейнер уже пересоздан на новом образе —
        // при явном падении пересоздаём ещё раз на старом (образ ещё не удалён, prune в конце задачи)
        await _compose.RestoreAsync(previousCompose);
        if (health.DefiniteFailure)
        {
            await _docker.ComposeUpAsync(step.Service);
            step.RolledBack = true;
            _logger.LogWarning("Сервис {Service} откачен на предыдущую ветку после неудачного переключения", step.Service);
            throw new InvalidOperationException($"{health.Reason}. Переключение откачено, сервис возвращён на прежнюю ветку");
        }

        throw new InvalidOperationException($"{health.Reason}. Ветка в docker-compose.yml возвращена на прежнюю");
    }

    private sealed record HealthResult(bool Ok, bool DefiniteFailure, string Reason);

    /// <summary>
    /// Дождаться, пока контейнер станет здоровым:
    /// - exited/dead/restarting — явное падение (crash-loop);
    /// - Docker healthcheck unhealthy — явное падение;
    /// - running без healthcheck — два успешных опроса подряд (ловим мгновенный краш);
    /// - таймаут — неуспех без отката (не доказательство поломки).
    /// </summary>
    private async Task<HealthResult> WaitHealthyAsync(string service, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + HealthTimeout;
        var sawRunning = false;

        await Task.Delay(InitialSettleDelay, ct);

        while (true)
        {
            string state;
            string health;
            try
            {
                (state, health) = await _docker.InspectStateAsync(service);
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow >= deadline)
                    return new HealthResult(false, false, $"Не удалось проверить состояние контейнера: {ex.Message}");
                await Task.Delay(HealthPollInterval, ct);
                continue;
            }

            if (state is "exited" or "dead")
                return new HealthResult(false, true, $"Контейнер не запустился (state={state})");
            if (state == "restarting")
                return new HealthResult(false, true, "Контейнер падает и перезапускается (crash-loop)");
            if (health == "unhealthy")
                return new HealthResult(false, true, "Docker healthcheck: unhealthy");

            if (state == "running" && health == "healthy")
                return new HealthResult(true, false, string.Empty);

            if (state == "running" && health == "none")
            {
                if (sawRunning)
                    return new HealthResult(true, false, string.Empty);
                sawRunning = true;
            }
            // state == running + health == starting — ещё проверяемся

            if (DateTime.UtcNow >= deadline)
                return new HealthResult(false, false,
                    $"Контейнер не стал здоровым за {HealthTimeout.TotalSeconds:N0} с (state={state}, health={health})");

            await Task.Delay(HealthPollInterval, ct);
        }
    }
}
