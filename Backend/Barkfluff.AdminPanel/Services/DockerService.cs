using Barkfluff.AdminPanel.Models.Dtos;

using Microsoft.Extensions.Caching.Memory;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Сервис для управления Docker контейнерами и образами через Docker CLI
/// </summary>
public class DockerService
{
    private const string ContainersCacheKey = "docker:containers:list";
    private static readonly SemaphoreSlim ContainersGate = new(1, 1);
    private static readonly TimeSpan ContainersCacheTtl = TimeSpan.FromSeconds(10);

    private readonly ILogger<DockerService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IDockerCommandRunner _commands;
    private readonly DockerContainerNameResolver _containerNames;

    public DockerService(
        IMemoryCache cache,
        ILogger<DockerService> logger,
        IDockerCommandRunner commands,
        DockerContainerNameResolver containerNames)
    {
        _cache = cache;
        _logger = logger;
        _commands = commands;
        _containerNames = containerNames;
    }

    /// <summary>
    /// Получить имя образа контейнера admin-panel (для использования как helper)
    /// </summary>
    private async Task<string> GetAdminPanelImageAsync()
    {
        var adminPanel = await _containerNames.ResolveContainerNameAsync("admin-panel");
        var image = await RunDockerCommandAsync("inspect", "--format", "{{.Config.Image}}", adminPanel);
        return image.Trim();
    }

    /// <summary>
    /// Получить host-путь bind mount'а по destination-пути внутри контейнера.
    /// Использует docker inspect для извлечения реальных путей на хосте.
    /// </summary>
    private async Task<string> GetMountSourceAsync(string containerName, string destination)
    {
        // Go template: ищем Mount с нужным Destination и возвращаем Source
        var template = "{{range .Mounts}}{{if eq .Destination \"" + destination + "\"}}{{.Source}}{{end}}{{end}}";
        var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
        var source = await RunDockerCommandAsync("inspect", "--format", template, resolvedName);
        return source.Trim();
    }

    /// <summary>
    /// Получить список всех контейнеров и их статусы.
    /// Результат кэшируется на <see cref="ContainersCacheTtl"/>; параллельные запросы
    /// на cache-miss координируются через <see cref="ContainersGate"/> (один docker ps).
    /// </summary>
    public async Task<List<ContainerStatusDto>> GetContainersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(ContainersCacheKey, out List<ContainerStatusDto>? cached) && cached is not null)
            return cached;

        await ContainersGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(ContainersCacheKey, out cached) && cached is not null)
                return cached;

            var containers = await LoadContainersFromDockerAsync(cancellationToken);
            _cache.Set(ContainersCacheKey, containers, ContainersCacheTtl);
            return containers;
        }
        finally
        {
            ContainersGate.Release();
        }
    }

    private async Task<List<ContainerStatusDto>> LoadContainersFromDockerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = (await _containerNames.GetContainersAsync(cancellationToken)).ToList();
            await PopulateImageDigestsAsync(containers);
            return containers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения списка контейнеров");
            throw;
        }
    }

    /// <summary>
    /// Сбросить кэш списка контейнеров. Вызывать после мутаций (start/stop/restart/pull).
    /// </summary>
    private void InvalidateContainersCache()
    {
        _cache.Remove(ContainersCacheKey);
        _containerNames.Invalidate();
    }

    /// <summary>
    /// Получить статус конкретного контейнера
    /// </summary>
    public async Task<ContainerStatusDto?> GetContainerStatusAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName, cancellationToken);
            var containers = await GetContainersAsync(cancellationToken);
            return containers.FirstOrDefault(c =>
                c.Name == resolvedName ||
                c.Name == $"/{resolvedName}" ||
                c.Id.StartsWith(resolvedName, StringComparison.OrdinalIgnoreCase)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения статуса контейнера {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Запустить контейнер
    /// </summary>
    public async Task<ContainerActionResponseDto> StartContainerAsync(string containerName)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
            await RunDockerCommandAsync("start", resolvedName);
            InvalidateContainersCache();

            _logger.LogInformation("Контейнер {ContainerName} запущен", containerName);

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Контейнер {containerName} успешно запущен"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка запуска контейнера {ContainerName}", containerName);
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = $"Ошибка запуска контейнера {containerName}",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Остановить контейнер
    /// </summary>
    public async Task<ContainerActionResponseDto> StopContainerAsync(string containerName)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
            await RunDockerCommandAsync("stop", "-t", "30", resolvedName);
            InvalidateContainersCache();

            _logger.LogInformation("Контейнер {ContainerName} остановлен", containerName);

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Контейнер {containerName} успешно остановлен"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка остановки контейнера {ContainerName}", containerName);
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = $"Ошибка остановки контейнера {containerName}",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Перезапустить контейнер
    /// </summary>
    public async Task<ContainerActionResponseDto> RestartContainerAsync(string containerName)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
            await RunDockerCommandAsync("restart", "-t", "30", resolvedName);
            InvalidateContainersCache();

            _logger.LogInformation("Контейнер {ContainerName} перезапущен", containerName);

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Контейнер {containerName} успешно перезапущен"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка перезапуска контейнера {ContainerName}", containerName);
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = $"Ошибка перезапуска контейнера {containerName}",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Обновить образ и пересоздать контейнер через docker compose.
    /// Разбито на отдельные шаги (pull/up), которыми управляет DeployJobService.
    /// </summary>
    public async Task ComposePullAsync(string serviceName)
    {
        await RunDockerComposeCommandAsync("pull", serviceName);
        InvalidateContainersCache();
        _logger.LogInformation("Образ для сервиса {ServiceName} успешно обновлен", serviceName);
    }

    /// <summary>
    /// Пересоздать контейнер через docker compose up --force-recreate
    /// </summary>
    public async Task ComposeUpAsync(string serviceName)
    {
        await RunDockerComposeCommandAsync("up", "--force-recreate", "--no-deps", "--build", "-d", serviceName);
        _logger.LogInformation("Контейнер сервиса {ServiceName} успешно пересоздан", serviceName);
        InvalidateContainersCache();
    }

    /// <summary>
    /// Очистить неиспользуемые образы (docker image prune -f).
    /// Вызывается только после завершения деплой-задачи: старые образы нужны для отката.
    /// </summary>
    public async Task PruneImagesAsync()
    {
        await RunDockerCommandAsync("image", "prune", "-f");
        _logger.LogInformation("Неиспользуемые образы очищены");
    }

    /// <summary>
    /// Состояние контейнера и его Docker healthcheck.
    /// Health = none, если HEALTHCHECK в образе не задан.
    /// </summary>
    public async Task<(string State, string Health)> InspectStateAsync(string containerName)
    {
        var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
        var output = await RunDockerCommandAsync("inspect", "--format",
            "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}", resolvedName);
        var parts = output.Split('|');
        return (parts[0], parts.Length > 1 ? parts[1] : "none");
    }

    /// <summary>
    /// ID образа запущенного контейнера (null — контейнер не существует)
    /// </summary>
    public async Task<string?> GetContainerImageIdAsync(string containerName)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
            return await RunDockerCommandAsync("inspect", "--format", "{{.Image}}", resolvedName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ссылка на образ контейнера (например, docker.barkfluff.com/barkfluff-users:latest); null — контейнер не существует
    /// </summary>
    public async Task<string?> GetContainerImageReferenceAsync(string containerName)
    {
        try
        {
            var resolvedName = await _containerNames.ResolveContainerNameAsync(containerName);
            return await RunDockerCommandAsync("inspect", "--format", "{{.Config.Image}}", resolvedName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Перемаркировать старый образ под текущую ссылку — откат без правки compose-файла
    /// </summary>
    public Task TagImageAsync(string imageId, string reference) =>
        RunDockerCommandAsync("tag", imageId, reference);

    public Task<string> ResolveContainerNameAsync(string serviceOrContainerName, CancellationToken cancellationToken = default) =>
        _containerNames.ResolveContainerNameAsync(serviceOrContainerName, cancellationToken);

    public Task<string> ResolveServiceNameAsync(string containerOrServiceName, CancellationToken cancellationToken = default) =>
        _containerNames.ResolveServiceNameAsync(containerOrServiceName, cancellationToken);

    public Task<string?> TryResolveComposeServiceNameAsync(string containerName, CancellationToken cancellationToken = default) =>
        _containerNames.TryResolveComposeServiceNameAsync(containerName, cancellationToken);

    /// <summary>
    /// Выполнить Docker команду и вернуть результат.
    /// Использует ArgumentList чтобы каждый аргумент передавался OS буквально, без shell-интерпретации.
    /// </summary>
    private Task<string> RunDockerCommandAsync(params string[] args) =>
        _commands.RunAsync(CancellationToken.None, args);

    /// <summary>
    /// Выполнить Docker Compose через временный helper-контейнер.
    /// Docker CLI работает внутри admin-panel, поэтому его текущая директория
    /// не совпадает с host-каталогом Compose. Монтируем проект по реальному
    /// host-пути, чтобы относительные bind mounts разрешались на хосте.
    /// </summary>
    private async Task<string> RunDockerComposeCommandAsync(params string[] args)
    {
        var helperImage = await GetAdminPanelImageAsync();
        var dockerSocket = await GetMountSourceAsync("admin-panel", "/var/run/docker.sock");
        var composeFile = await GetMountSourceAsync("admin-panel", "/docker-compose.yml");
        var envFile = await GetMountSourceAsync("admin-panel", "/.env");
        var projectDirectory = Path.GetDirectoryName(composeFile);

        if (string.IsNullOrWhiteSpace(dockerSocket)
            || string.IsNullOrWhiteSpace(composeFile)
            || string.IsNullOrWhiteSpace(envFile)
            || string.IsNullOrWhiteSpace(projectDirectory)
            || !Path.IsPathRooted(dockerSocket)
            || !Path.IsPathRooted(composeFile)
            || !Path.IsPathRooted(envFile)
            || !Path.IsPathRooted(projectDirectory))
        {
            throw new InvalidOperationException("Не удалось определить реальные host-пути Docker Compose.");
        }

        var composeFiles = await GetActiveComposeFilesAsync(composeFile);
        var dockerArguments = new List<string>
        {
            "run", "--rm", "--user", "root",
            "--mount", $"type=bind,src={dockerSocket},dst=/var/run/docker.sock",
        };

        DockerComposeInvocation.AddReadOnlyMounts(dockerArguments, composeFiles, envFile);

        dockerArguments.Add("--entrypoint");
        dockerArguments.Add("docker");
        dockerArguments.Add(helperImage);
        dockerArguments.AddRange(DockerComposeInvocation.CreateCommandArguments(composeFiles, envFile, args));

        return await RunDockerCommandAsync(dockerArguments.ToArray());
    }

    private async Task<IReadOnlyList<string>> GetActiveComposeFilesAsync(string baseComposeFile)
    {
        const string configFilesLabel = "com.docker.compose.project.config_files";
        var template = "{{if .Config.Labels}}{{index .Config.Labels \"" + configFilesLabel + "\"}}{{end}}";
        var adminPanel = await _containerNames.ResolveContainerNameAsync("admin-panel");
        var activeFiles = await RunDockerCommandAsync("inspect", "--format", template, adminPanel);
        return DockerComposeInvocation.ResolveFiles(baseComposeFile, activeFiles);
    }

    /// <summary>
    /// Удалить helper-контейнер если он ещё существует (игнорировать ошибку если нет)
    /// </summary>
    private async Task TryRemoveHelperContainerAsync(string containerName)
    {
        try
        {
            await RunDockerCommandAsync("rm", "-f", containerName);
        }
        catch
        {
            // Контейнер не существует — это нормально
        }
    }

    private async Task PopulateImageDigestsAsync(IEnumerable<ContainerStatusDto> containers)
    {
        await Task.WhenAll(containers
            .Where(container => container.Image.StartsWith("docker.barkfluff.com/barkfluff-", StringComparison.OrdinalIgnoreCase))
            .Select(PopulateImageDigestAsync));
    }

    private async Task PopulateImageDigestAsync(ContainerStatusDto container)
    {
        try
        {
            var imageId = await RunDockerCommandAsync("inspect", "--format", "{{.Image}}", container.Id);
            var repoDigests = await RunDockerCommandAsync(
                "image", "inspect", "--format", "{{join .RepoDigests \"\\n\"}}", imageId);

            var imageRepository = container.Image[..container.Image.LastIndexOf(':')];
            container.ImageDigest = repoDigests
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(digest => digest.StartsWith($"{imageRepository}@", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось получить digest образа {Image}", container.Image);
        }
    }

    /// <summary>
    /// Перезапустить саму админ-панель через detached helper-контейнер
    /// </summary>
    public async Task<ContainerActionResponseDto> RestartAdminPanelAsync()
    {
        try
        {
            _logger.LogInformation("Запуск перезапуска админ-панели через helper-контейнер...");

            var helperImage = await GetAdminPanelImageAsync();
            var dockerSock = await GetMountSourceAsync("admin-panel", "/var/run/docker.sock");
            var adminPanel = await _containerNames.ResolveContainerNameAsync("admin-panel");

            // Удаляем старый хелпер если он ещё существует
            await TryRemoveHelperContainerAsync("admin-panel-restarter");

            await RunDockerCommandAsync(
                "run", "-d", "--rm",
                "--name", "admin-panel-restarter",
                "--user", "root",
                "-v", $"{dockerSock}:/var/run/docker.sock",
                "--entrypoint", "sh",
                helperImage,
                "-c", $"sleep 2 && docker restart {adminPanel}"
            );

            _logger.LogInformation("Helper-контейнер для перезапуска запущен");

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = "Админ-панель будет перезапущена"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка перезапуска админ-панели");
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = "Ошибка перезапуска админ-панели",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Обновить образ и пересоздать админ-панель через detached helper-контейнер.
    /// Файлы монтируются по их реальным хостовым путям, чтобы docker compose
    /// корректно разрешал относительные пути (./.env) внутри compose-файла.
    /// </summary>
    public async Task<ContainerActionResponseDto> UpdateAdminPanelAsync()
    {
        try
        {
            _logger.LogInformation("Запуск обновления админ-панели через helper-контейнер...");

            var helperImage = await GetAdminPanelImageAsync();
            var dockerSock = await GetMountSourceAsync("admin-panel", "/var/run/docker.sock");
            var composeFile = await GetMountSourceAsync("admin-panel", "/docker-compose.yml");
            var envFile = await GetMountSourceAsync("admin-panel", "/.env");
            var composeFiles = await GetActiveComposeFilesAsync(composeFile);

            // Удаляем старый хелпер если он ещё существует
            await TryRemoveHelperContainerAsync("admin-panel-updater");

            var dockerArguments = new List<string>
            {
                "run", "-d", "--rm",
                "--name", "admin-panel-updater",
                "--user", "root",
                "--mount", $"type=bind,src={dockerSock},dst=/var/run/docker.sock",
            };
            DockerComposeInvocation.AddReadOnlyMounts(dockerArguments, composeFiles, envFile);
            dockerArguments.AddRange(["--entrypoint", "sh", helperImage, "-c"]);
            var pullCommand = DockerComposeInvocation.ToShellCommand(
                DockerComposeInvocation.CreateCommandArguments(composeFiles, envFile, "pull", "admin-panel"));
            var upCommand = DockerComposeInvocation.ToShellCommand(
                DockerComposeInvocation.CreateCommandArguments(composeFiles, envFile, "up", "--force-recreate", "--no-deps", "-d", "admin-panel"));
            dockerArguments.Add($"sleep 2 && docker {pullCommand} && docker {upCommand} && docker image prune -f");
            await RunDockerCommandAsync(dockerArguments.ToArray());

            _logger.LogInformation("Helper-контейнер для обновления запущен");

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = "Обновление админ-панели запущено"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления админ-панели");
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = "Ошибка обновления админ-панели",
                ErrorDetails = ex.Message
            };
        }
    }
}
