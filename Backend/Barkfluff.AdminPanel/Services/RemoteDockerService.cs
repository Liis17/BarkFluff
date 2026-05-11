using Barkfluff.AdminPanel.Models.Dtos;

using Microsoft.Extensions.Options;

using Renci.SshNet;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

public record RemoteContainerInfo(string Label, string ServiceName, string ComposePath, string WorkDir);

public record RemoteServerInfoDto(string Host, int Port, string Username, string Password);

public class RemoteContainerStatusDto
{
    public string Label { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class RemoteDockerService
{
    private readonly IOptionsMonitor<RemoteServersSettings> _settings;
    private readonly ILogger<RemoteDockerService> _logger;

    private static readonly Dictionary<string, List<RemoteContainerInfo>> ServerContainers = new()
    {
        ["navigator"] =
        [
            new("Navigator", "navigator-dev",
                "/root/hueker/barkfluff/docker-compose-dev.yml",
                "/root/hueker/barkfluff")
        ],
        ["msk"] =
        [
            new("ClientStorage", "clientstorage-dev",
                "/root/hueker/clientstorage/docker-compose-dev.yml",
                "/root/hueker/clientstorage"),
            new("WebServer", "webserver-dev",
                "/root/hueker/web/docker-compose-dev.yml",
                "/root/hueker/web"),
            new("Nginx", "nginx",
                "/root/hueker/nginx/docker-compose-msk.yml",
                "/root/hueker/nginx")
        ]
    };

    public RemoteDockerService(IOptionsMonitor<RemoteServersSettings> settings, ILogger<RemoteDockerService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<RemoteContainerStatusDto>> GetContainersStatusAsync(string server)
    {
        if (!ServerContainers.TryGetValue(server, out var containers))
            return [];

        var serverConfig = GetServerConfig(server);
        if (string.IsNullOrEmpty(serverConfig.Host))
            return containers.Select(c => new RemoteContainerStatusDto
            {
                Label = c.Label,
                ServiceName = c.ServiceName,
                State = "unconfigured",
                Status = "Сервер не настроен"
            }).ToList();

        var results = new List<RemoteContainerStatusDto>();

        foreach (var container in containers)
        {
            try
            {
                // Используем docker ps с фильтрацией по docker compose labels — стабильный NDJSON-формат,
                // не зависит от версии docker compose и не путается с JSON-array выводом compose ps.
                var cmd = $"docker ps --all" +
                          $" --filter \"label=com.docker.compose.service={container.ServiceName}\"" +
                          $" --filter \"label=com.docker.compose.project.working_dir={container.WorkDir}\"" +
                          $" --format \"{{{{json .}}}}\"";

                var (stdout, _, _) = await RunSshCommandAsync(serverConfig, cmd);

                results.Add(ParseDockerPsOutput(stdout, container));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка получения статуса {ServiceName} на {Server}", container.ServiceName, server);
                results.Add(new RemoteContainerStatusDto
                {
                    Label = container.Label,
                    ServiceName = container.ServiceName,
                    State = "error",
                    Status = $"Ошибка: {ex.Message}"
                });
            }
        }

        return results;
    }

    public async Task<ContainerActionResponseDto> StartAsync(string server, string serviceName)
    {
        // Используем "up -d" вместо "start" — работает и для остановленных и для удалённых контейнеров
        return await ExecuteComposeActionAsync(server, serviceName, "up_d");
    }

    public async Task<ContainerActionResponseDto> StopAsync(string server, string serviceName)
    {
        return await ExecuteComposeActionAsync(server, serviceName, "stop");
    }

    public async Task<ContainerActionResponseDto> RestartAsync(string server, string serviceName)
    {
        return await ExecuteComposeActionAsync(server, serviceName, "restart");
    }

    public async Task<ContainerActionResponseDto> PullAndRecreateAsync(string server, string serviceName)
    {
        var serverConfig = GetServerConfig(server);
        if (string.IsNullOrEmpty(serverConfig.Host))
            return Fail($"Сервер {server} не настроен");

        var container = FindContainer(server, serviceName);
        if (container is null)
            return Fail($"Сервис {serviceName} не найден на сервере {server}");

        try
        {
            var workDir = EscapeShell(container.WorkDir);
            var composePath = EscapeShell(container.ComposePath);
            var service = EscapeShell(container.ServiceName);

            var cmd = $"cd {workDir}" +
                      $" && docker compose -f {composePath} pull {service}" +
                      $" && docker compose -f {composePath} up --force-recreate -d {service}" +
                      $" && docker image prune -f";

            var (_, stderr, exit) = await RunSshCommandAsync(serverConfig, cmd);

            if (exit != 0)
                return Fail($"Ошибка обновления {serviceName}", stderr);

            _logger.LogInformation("Сервис {ServiceName} на {Server} обновлён", serviceName, server);
            return Ok($"Сервис {serviceName} успешно обновлён");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления {ServiceName} на {Server}", serviceName, server);
            return Fail($"Ошибка обновления {serviceName}", ex.Message);
        }
    }

    private async Task<ContainerActionResponseDto> ExecuteComposeActionAsync(string server, string serviceName, string action)
    {
        var serverConfig = GetServerConfig(server);
        if (string.IsNullOrEmpty(serverConfig.Host))
            return Fail($"Сервер {server} не настроен");

        var container = FindContainer(server, serviceName);
        if (container is null)
            return Fail($"Сервис {serviceName} не найден на сервере {server}");

        try
        {
            var workDir = EscapeShell(container.WorkDir);
            var composePath = EscapeShell(container.ComposePath);
            var service = EscapeShell(container.ServiceName);

            // up_d → "up -d" (два аргумента для compose), остальные — одиночные глаголы
            var composeArgs = action == "up_d" ? $"up -d {service}" : $"{action} {service}";
            var cmd = $"cd {workDir} && docker compose -f {composePath} {composeArgs}";

            var (_, stderr, exit) = await RunSshCommandAsync(serverConfig, cmd);

            if (exit != 0)
                return Fail($"Ошибка {action} {serviceName}", stderr);

            _logger.LogInformation("Действие {Action} над {ServiceName} на {Server} выполнено", action, serviceName, server);
            return Ok($"Действие над {serviceName} выполнено успешно");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка {Action} {ServiceName} на {Server}", action, serviceName, server);
            return Fail($"Ошибка {action} {serviceName}", ex.Message);
        }
    }

    private async Task<(string stdout, string stderr, int exit)> RunSshCommandAsync(RemoteServerSettings config, string command)
    {
        var passwordAuth = new PasswordAuthenticationMethod(config.Username, config.Password);

        // Keyboard-interactive как fallback — многие серверы (Ubuntu 22.04+) принимают только этот метод,
        // даже если с виду это обычный пароль. Стандартный ssh-клиент пробует оба метода автоматически.
        var kbdAuth = new KeyboardInteractiveAuthenticationMethod(config.Username);
        kbdAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
                prompt.Response = config.Password;
        };

        var sshConn = new Renci.SshNet.ConnectionInfo(
            config.Host, config.Port, config.Username, passwordAuth, kbdAuth);

        using var client = new SshClient(sshConn);

        await Task.Run(() => client.Connect());
        try
        {
            using var cmd = client.RunCommand(command);
            return (cmd.Result, cmd.Error, cmd.ExitStatus ?? 0);
        }
        finally
        {
            client.Disconnect();
        }
    }

    private RemoteContainerStatusDto ParseDockerPsOutput(string output, RemoteContainerInfo container)
    {
        var dto = new RemoteContainerStatusDto
        {
            Label = container.Label,
            ServiceName = container.ServiceName,
            State = "not_found",
            Status = "Не найден"
        };

        if (string.IsNullOrWhiteSpace(output))
            return dto;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("State", out var state))
                    dto.State = state.GetString() ?? "unknown";

                if (root.TryGetProperty("Status", out var status))
                    dto.Status = status.GetString() ?? string.Empty;

                break;
            }
            catch
            {
                // строка не является JSON-объектом — пропускаем
            }
        }

        return dto;
    }

    public RemoteServerInfoDto GetServerInfo(string server)
    {
        var config = GetServerConfig(server);
        return new RemoteServerInfoDto(config.Host, config.Port, config.Username, config.Password);
    }

    private RemoteServerSettings GetServerConfig(string server) => server switch
    {
        "navigator" => _settings.CurrentValue.Navigator,
        "msk" => _settings.CurrentValue.Msk,
        _ => new RemoteServerSettings()
    };

    private static RemoteContainerInfo? FindContainer(string server, string serviceName)
    {
        if (!ServerContainers.TryGetValue(server, out var list))
            return null;
        return list.FirstOrDefault(c => c.ServiceName == serviceName);
    }

    private static string EscapeShell(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static ContainerActionResponseDto Ok(string message) =>
        new() { Success = true, Message = message };

    private static ContainerActionResponseDto Fail(string message, string? details = null) =>
        new() { Success = false, Message = message, ErrorDetails = details };
}
