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
    /// Получить список всех контейнеров и их статусы
    /// </summary>
    public async Task<List<ContainerStatusDto>> GetContainersAsync()
    {
        try
        {
            var json = await RunDockerCommandAsync("ps", "--all", "--format", "{{json .}}");
            var containers = ParseDockerPsOutput(json);
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

    /// <summary>
    /// Перезапустить саму админ-панель (асинхронно, чтобы успеть отправить ответ)
    /// </summary>
    public async Task<ContainerActionResponseDto> RestartAdminPanelAsync()
    {
        try
        {
            _logger.LogInformation("Запуск перезапуска админ-панели...");

            // Запускаем перезапуск в фоне, чтобы успеть отправить ответ
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // Небольшая задержка чтобы ответ успел уйти
                await RunDockerCommandAsync("restart", "-t", "30", "admin-panel");
                _logger.LogInformation("Админ-панель перезапущена");
            });

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
    /// Обновить образ и пересоздать админ-панель (асинхронно, чтобы успеть отправить ответ)
    /// </summary>
    public async Task<ContainerActionResponseDto> UpdateAdminPanelAsync()
    {
        try
        {
            _logger.LogInformation("Запуск обновления админ-панели...");

            // Запускаем обновление в фоне, чтобы успеть отправить ответ
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // Небольшая задержка чтобы ответ успел уйти
                var result = await PullImageAndRecreateContainerAsync("admin-panel");
                if (result.Success)
                {
                    _logger.LogInformation("Админ-панель обновлена");
                }
                else
                {
                    _logger.LogError("Ошибка обновления админ-панели: {Error}", result.ErrorDetails);
                }
            });

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
