using Barkfluff.AdminPanel.Models.Dtos;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Сервис для управления Docker контейнерами и образами через Docker CLI
/// </summary>
public class DockerService
{
    private readonly ILogger<DockerService> _logger;

    public DockerService(ILogger<DockerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Получить имя образа контейнера admin-panel (для использования как helper)
    /// </summary>
    private async Task<string> GetAdminPanelImageAsync()
    {
        var image = await RunDockerCommandAsync("inspect", "--format", "{{.Config.Image}}", "admin-panel");
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
        var source = await RunDockerCommandAsync("inspect", "--format", template, containerName);
        return source.Trim();
    }

    /// <summary>
    /// Получить список всех контейнеров и их статусы
    /// </summary>
    public async Task<List<ContainerStatusDto>> GetContainersAsync()
    {
        try
        {
            var json = await RunDockerCommandAsync("ps", "--all", "--format", "{{json .}}");
            var containers = ParseDockerPsOutput(json);
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
    /// Получить статус конкретного контейнера
    /// </summary>
    public async Task<ContainerStatusDto?> GetContainerStatusAsync(string containerName)
    {
        try
        {
            var containers = await GetContainersAsync();
            return containers.FirstOrDefault(c =>
                c.Name == containerName ||
                c.Name == $"/{containerName}" ||
                c.Id.StartsWith(containerName, StringComparison.OrdinalIgnoreCase)
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
            await RunDockerCommandAsync("start", containerName);

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
            await RunDockerCommandAsync("stop", "-t", "30", containerName);

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
            await RunDockerCommandAsync("restart", "-t", "30", containerName);

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
    /// Обновить образ и пересоздать контейнер через docker compose
    /// </summary>
    public async Task<ContainerActionResponseDto> PullImageAndRecreateContainerAsync(string containerName)
    {
        try
        {
            var serviceName = ConvertContainerNameToServiceName(containerName);

            _logger.LogInformation("Обновление контейнера {ContainerName} (сервис: {ServiceName})", containerName, serviceName);

            // 1. Pull нового образа через docker compose pull <service>
            // Используем --project-name=barkfluff чтобы указать имя проекта
            await RunDockerComposeCommandAsync("--project-name", "barkfluff", "--env-file", "/.env", "-f", "/docker-compose.yml", "pull", serviceName);
            _logger.LogInformation("Образ для сервиса {ServiceName} успешно обновлен", serviceName);

            // 2. Пересоздать контейнер через docker compose up --force-recreate --build -d <service>
            await RunDockerComposeCommandAsync("--project-name", "barkfluff", "--env-file", "/.env", "-f", "/docker-compose.yml", "up", "--force-recreate", "--build", "-d", serviceName);
            _logger.LogInformation("Контейнер {ContainerName} успешно пересоздан", containerName);

            // 3. Очистить неиспользуемые образы через docker image prune -f
            await RunDockerCommandAsync("image", "prune", "-f");
            _logger.LogInformation("Неиспользуемые образы очищены");

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Контейнер {containerName} успешно обновлен и пересоздан"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления и пересоздания контейнера {ContainerName}", containerName);
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = $"Ошибка обновления и пересоздания контейнера {containerName}",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Преобразовать имя контейнера в имя сервиса docker compose
    /// </summary>
    private string ConvertContainerNameToServiceName(string containerName)
    {
        // Маппинг имен контейнеров на имена сервисов
        var containerToServiceMap = new Dictionary<string, string>
        {
            { "beacon", "beacon" },
            { "configuration", "configuration" },
            { "files", "files" },
            { "identity", "identity" },
            { "messages", "messages" },
            { "notification", "notification" },
            { "users", "users" },
            { "fast-auth", "fast-auth" },
            { "updates", "updates" },
            { "onliner", "onliner" },
            { "federation", "federation" },
            { "web", "web" },
            { "seq", "seq" },
            { "minio", "minio" },
            { "rabbitmq", "rabbitmq" },
            { "redis", "redis" },
            { "postgres_barkfluff", "postgres" },
            { "admin-panel", "admin-panel" }
        };

        return containerToServiceMap.GetValueOrDefault(containerName, containerName);
    }

    /// <summary>
    /// Выполнить Docker команду и вернуть результат.
    /// Использует ArgumentList чтобы каждый аргумент передавался OS буквально, без shell-интерпретации.
    /// </summary>
    private async Task<string> RunDockerCommandAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Docker command failed: {errorBuilder}");
        }

        return outputBuilder.ToString().Trim();
    }

    /// <summary>
    /// Выполнить Docker Compose команду.
    /// Использует ArgumentList — каждый аргумент передаётся буквально.
    /// </summary>
    private async Task<string> RunDockerComposeCommandAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = "/" // Корневая директория
        };

        startInfo.ArgumentList.Add("compose");
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Docker compose command failed: {errorBuilder}");
        }

        return outputBuilder.ToString().Trim();
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

    /// <summary>
    /// Распарсить вывод docker ps --format json
    /// </summary>
    private List<ContainerStatusDto> ParseDockerPsOutput(string output)
    {
        var containers = new List<ContainerStatusDto>();

        if (string.IsNullOrEmpty(output))
            return containers;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            try
            {
                var json = line.Trim();
                if (string.IsNullOrEmpty(json))
                    continue;

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var container = new ContainerStatusDto
                {
                    Id = root.TryGetProperty("ID", out var id) ? id.GetString() ?? "" : "",
                    Name = root.TryGetProperty("Names", out var names) ? names.GetString() ?? "" : "",
                    Image = root.TryGetProperty("Image", out var image) ? image.GetString() ?? "" : "",
                    State = root.TryGetProperty("State", out var state) ? state.GetString() ?? "" : "",
                    Status = root.TryGetProperty("Status", out var status) ? status.GetString() ?? "" : "",
                    Ports = root.TryGetProperty("Ports", out var ports) ? ports.GetString() ?? "none" : "none",
                    CreatedAt = DateTime.UtcNow
                };

                containers.Add(container);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка парсинга строки контейнера: {Line}", line);
            }
        }

        return containers;
    }

    private async Task PopulateImageDigestsAsync(IEnumerable<ContainerStatusDto> containers)
    {
        await Task.WhenAll(containers
            .Where(container => container.Image.StartsWith("docker.barkfluff.com:5000/barkfluff-", StringComparison.OrdinalIgnoreCase))
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

            // Удаляем старый хелпер если он ещё существует
            await TryRemoveHelperContainerAsync("admin-panel-restarter");

            await RunDockerCommandAsync(
                "run", "-d", "--rm",
                "--name", "admin-panel-restarter",
                "--user", "root",
                "-v", $"{dockerSock}:/var/run/docker.sock",
                "--entrypoint", "sh",
                helperImage,
                "-c", "sleep 2 && docker restart admin-panel"
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
    /// Порядок запуска сервисов BarkFluff (configuration первым, так как остальные от него зависят)
    /// </summary>
    private static readonly string[] BarkFluffServicesOrder = new[]
    {
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
    };

    /// <summary>
    /// Перезапустить все сервисы BarkFluff последовательно, начиная с configuration
    /// </summary>
    public async Task<ContainerActionResponseDto> RestartAllServicesAsync()
    {
        try
        {
            _logger.LogInformation("Начинаем последовательный перезапуск всех сервисов BarkFluff");

            var results = new List<string>();
            var errors = new List<string>();

            foreach (var serviceName in BarkFluffServicesOrder)
            {
                try
                {
                    _logger.LogInformation("Перезапуск сервиса: {ServiceName}", serviceName);
                    await RunDockerCommandAsync("restart", "-t", "30", serviceName);
                    results.Add(serviceName);

                    // Небольшая пауза чтобы сервис успел запуститься
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка перезапуска сервиса {ServiceName}", serviceName);
                    errors.Add($"{serviceName}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                return new ContainerActionResponseDto
                {
                    Success = false,
                    Message = $"Перезапущено: {string.Join(", ", results)}. Ошибки: {string.Join("; ", errors)}",
                    ErrorDetails = string.Join("\n", errors)
                };
            }

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Все сервисы успешно перезапущены: {string.Join(", ", results)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при массовом перезапуске сервисов");
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = "Ошибка при массовом перезапуске сервисов",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Обновить образы и пересоздать все сервисы BarkFluff последовательно, начиная с configuration
    /// </summary>
    public async Task<ContainerActionResponseDto> UpdateAllServicesAsync()
    {
        try
        {
            _logger.LogInformation("Начинаем последовательное обновление всех сервисов BarkFluff");

            var results = new List<string>();
            var errors = new List<string>();

            foreach (var serviceName in BarkFluffServicesOrder)
            {
                try
                {
                    _logger.LogInformation("Обновление сервиса: {ServiceName}", serviceName);

                    // 1. Pull нового образа
                    await RunDockerComposeCommandAsync("--project-name", "barkfluff", "--env-file", "/.env", "-f", "/docker-compose.yml", "pull", serviceName);
                    _logger.LogInformation("Образ для сервиса {ServiceName} обновлен", serviceName);

                    // 2. Пересоздать контейнер
                    await RunDockerComposeCommandAsync("--project-name", "barkfluff", "--env-file", "/.env", "-f", "/docker-compose.yml", "up", "--force-recreate", "--build", "-d", serviceName);
                    _logger.LogInformation("Контейнер {ServiceName} пересоздан", serviceName);

                    results.Add(serviceName);

                    // Пауза чтобы сервис успел запуститься и инициализироваться
                    await Task.Delay(3000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обновления сервиса {ServiceName}", serviceName);
                    errors.Add($"{serviceName}: {ex.Message}");
                }
            }

            // Очистка неиспользуемых образов в конце
            try
            {
                await RunDockerCommandAsync("image", "prune", "-f");
                _logger.LogInformation("Неиспользуемые образы очищены");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось очистить неиспользуемые образы");
            }

            if (errors.Count > 0)
            {
                return new ContainerActionResponseDto
                {
                    Success = false,
                    Message = $"Обновлено: {string.Join(", ", results)}. Ошибки: {string.Join("; ", errors)}",
                    ErrorDetails = string.Join("\n", errors)
                };
            }

            return new ContainerActionResponseDto
            {
                Success = true,
                Message = $"Все сервисы успешно обновлены: {string.Join(", ", results)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при массовом обновлении сервисов");
            return new ContainerActionResponseDto
            {
                Success = false,
                Message = "Ошибка при массовом обновлении сервисов",
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

            // Удаляем старый хелпер если он ещё существует
            await TryRemoveHelperContainerAsync("admin-panel-updater");

            // Монтируем compose и env по их РЕАЛЬНЫМ хостовым путям,
            // чтобы относительные пути в compose-файле (./.env) разрешались корректно на хосте
            await RunDockerCommandAsync(
                "run", "-d", "--rm",
                "--name", "admin-panel-updater",
                "--user", "root",
                "-v", $"{dockerSock}:/var/run/docker.sock",
                "-v", $"{composeFile}:{composeFile}:ro",
                "-v", $"{envFile}:{envFile}:ro",
                "--entrypoint", "sh",
                helperImage,
                "-c", $"sleep 2 && docker compose --project-name barkfluff --env-file {envFile} -f {composeFile} pull admin-panel && docker compose --project-name barkfluff --env-file {envFile} -f {composeFile} up --force-recreate -d admin-panel && docker image prune -f"
            );

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
