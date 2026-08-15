using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

using Microsoft.Extensions.Caching.Memory;

using System.Collections.Concurrent;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

public class RemoteDockerService
{
    private static readonly TimeSpan ContainersCacheTtl = TimeSpan.FromSeconds(10);

    private readonly RemoteDockerDbContext _db;
    private readonly IRemoteSshClient _sshClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RemoteDockerService> _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _serverLocks = new();

    public RemoteDockerService(RemoteDockerDbContext db, IRemoteSshClient sshClient, IMemoryCache cache, ILogger<RemoteDockerService> logger)
    {
        _db = db;
        _sshClient = sshClient;
        _cache = cache;
        _logger = logger;
    }

    private static string ContainersCacheKey(Guid serverId) => $"remote-docker:ps:{serverId}";
    private SemaphoreSlim GetServerLock(Guid serverId) =>
        _serverLocks.GetOrAdd(serverId, _ => new SemaphoreSlim(1, 1));

    public IReadOnlyList<RemoteServerDto> GetServers() => _db.Servers.FindAll()
        .OrderBy(x => x.Name)
        .Select(ToDto)
        .ToList();

    public async Task<RemoteServerDto> CreateServerAsync(SaveRemoteServerRequest request, CancellationToken cancellationToken = default)
    {
        ValidateServerRequest(request, passwordRequired: true);
        EnsureServerNameIsAvailable(request.Name, null);

        var server = new RemoteServer
        {
            Name = request.Name.Trim(),
            Host = request.Host.Trim(),
            Port = request.Port,
            Username = request.Username.Trim(),
            Password = request.Password!
        };

        await _sshClient.TestConnectionAsync(server, cancellationToken);
        _db.Servers.Insert(server);
        return ToDto(server);
    }

    public async Task<RemoteServerDto?> UpdateServerAsync(Guid serverId, SaveRemoteServerRequest request, CancellationToken cancellationToken = default)
    {
        var server = _db.Servers.FindById(serverId);
        if (server is null)
            return null;

        ValidateServerRequest(request, passwordRequired: false);
        EnsureServerNameIsAvailable(request.Name, serverId);

        server.Name = request.Name.Trim();
        server.Host = request.Host.Trim();
        server.Port = request.Port;
        server.Username = request.Username.Trim();
        if (!string.IsNullOrWhiteSpace(request.Password))
            server.Password = request.Password;
        server.UpdatedAtUtc = DateTime.UtcNow;

        await _sshClient.TestConnectionAsync(server, cancellationToken);
        _db.Servers.Update(server);
        return ToDto(server);
    }

    public bool DeleteServer(Guid serverId)
    {
        _db.Containers.DeleteMany(x => x.ServerId == serverId);
        return _db.Servers.Delete(serverId);
    }

    public async Task<IReadOnlyList<DiscoveredRemoteContainerDto>> DiscoverContainersAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var server = RequireServer(serverId);
        var trackedNames = _db.Containers.Find(x => x.ServerId == serverId)
            .Select(x => x.ContainerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var containers = await ListContainersWithComposeMetadataAsync(server, cancellationToken);
        return containers.Select(x => x.Container)
            .Where(x => !trackedNames.Contains(x.ContainerName))
            .ToList();
    }

    public async Task<RemoteContainerStatusDto?> AddContainerAsync(Guid serverId, string containerName, CancellationToken cancellationToken = default)
    {
        var server = RequireServer(serverId);
        var normalizedName = containerName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Не указано имя контейнера");
        if (_db.Containers.Exists(x => x.ServerId == serverId && x.ContainerName == normalizedName))
            throw new InvalidOperationException("Контейнер уже добавлен");

        var found = (await ListContainersWithComposeMetadataAsync(server, cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Container.ContainerName, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (found is null)
            return null;

        var container = new RemoteContainer
        {
            ServerId = serverId,
            ContainerName = found.Container.ContainerName,
            ComposeServiceName = found.Metadata.ServiceName,
            ComposeFiles = found.Metadata.ConfigFiles,
            ComposeWorkingDirectory = found.Metadata.WorkingDirectory
        };
        _db.Containers.Insert(container);
        return ToStatusDto(container, found.Container.State, found.Container.Status);
    }

    public async Task<IReadOnlyList<RemoteContainerStatusDto>> GetContainersStatusAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var server = RequireServer(serverId);
        var containers = _db.Containers.Find(x => x.ServerId == serverId)
            .OrderBy(x => x.ContainerName)
            .ToList();
        if (containers.Count == 0)
            return [];

        var currentContainers = (await ListContainersAsync(server, cancellationToken))
            .ToDictionary(x => x.ContainerName, StringComparer.OrdinalIgnoreCase);

        return containers.Select(container => currentContainers.TryGetValue(container.ContainerName, out var status)
                ? ToStatusDto(container, status.State, status.Status)
                : ToStatusDto(container, "not_found", "Не найден"))
            .ToList();
    }

    public bool DeleteContainer(Guid serverId, Guid containerId)
    {
        var container = _db.Containers.FindById(containerId);
        return container is not null && container.ServerId == serverId && _db.Containers.Delete(containerId);
    }

    public Task<IRemoteSshShell> OpenShellAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var server = RequireServer(serverId);
        return _sshClient.OpenShellAsync(server, cancellationToken);
    }

    public async Task<ContainerActionResponseDto> ExecuteActionAsync(Guid serverId, Guid containerId, string action,
        CancellationToken cancellationToken = default)
    {
        var server = RequireServer(serverId);
        var container = _db.Containers.FindById(containerId);
        if (container is null || container.ServerId != serverId)
            return Fail("Контейнер не найден");

        try
        {
            string command;
            switch (action)
            {
                case "start":
                    command = $"docker start {EscapeShell(container.ContainerName)}";
                    break;
                case "stop":
                    command = $"docker stop -t 30 {EscapeShell(container.ContainerName)}";
                    break;
                case "restart":
                    command = $"docker restart -t 30 {EscapeShell(container.ContainerName)}";
                    break;
                case "pull":
                    if (!container.CanUpdate)
                        return Fail("Обновление доступно только для контейнеров Docker Compose");
                    command = BuildComposeUpdateCommand(container);
                    break;
                default:
                    return Fail("Неизвестное действие");
            }

            var result = await _sshClient.RunAsync(server, command, cancellationToken);
            if (result.ExitCode != 0)
                return Fail($"Ошибка {ActionLabel(action)} контейнера {container.ContainerName}", result.Stderr);

            InvalidateContainersCache(serverId);
            _logger.LogInformation("Действие {Action} над {ContainerName} на {Server} выполнено", action, container.ContainerName, server.Name);
            return Ok($"Контейнер {container.ContainerName} успешно {ActionLabel(action)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка {Action} контейнера {ContainerName} на {Server}", action, container.ContainerName, server.Name);
            return Fail($"Ошибка {ActionLabel(action)} контейнера {container.ContainerName}", ex.Message);
        }
    }

    /// <summary>
    /// Список контейнеров сервера по <c>docker ps</c>. Результат кэшируется на
    /// <see cref="ContainersCacheTtl"/>; параллельные опросы одного сервера на cache-miss
    /// координируются через per-server <see cref="SemaphoreSlim"/> (один SSH-вызов).
    /// </summary>
    private async Task<List<DiscoveredRemoteContainerDto>> ListContainersAsync(RemoteServer server, CancellationToken cancellationToken)
    {
        var cacheKey = ContainersCacheKey(server.Id);
        if (_cache.TryGetValue(cacheKey, out List<DiscoveredRemoteContainerDto>? cached) && cached is not null)
            return cached;

        var gate = GetServerLock(server.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return cached;

            var result = await _sshClient.RunAsync(server, "docker ps --all --format '{{json .}}'", cancellationToken);
            ThrowIfFailed(result, "Не удалось получить список Docker-контейнеров");
            var containers = ParseDockerPsOutput(result.Stdout);
            _cache.Set(cacheKey, containers, ContainersCacheTtl);
            return containers;
        }
        finally
        {
            gate.Release();
        }
    }

    private void InvalidateContainersCache(Guid serverId) => _cache.Remove(ContainersCacheKey(serverId));

    private async Task<List<DiscoveredContainerWithMetadata>> ListContainersWithComposeMetadataAsync(
        RemoteServer server, CancellationToken cancellationToken)
    {
        const string separator = "__BARKFLUFF_REMOTE_CONTAINER_LABELS__";
        var result = await _sshClient.RunAsync(server,
            "docker ps --all --format '{{json .}}' || exit $?; " +
            $"printf '\\n{separator}\\n'; " +
            "docker ps --all --quiet | xargs -r docker inspect --format '{{.Name}}\\t{{json .Config.Labels}}' || true",
            cancellationToken);
        ThrowIfFailed(result, "Не удалось получить список Docker-контейнеров");

        var separatorIndex = result.Stdout.IndexOf(separator, StringComparison.Ordinal);
        var containersOutput = separatorIndex < 0 ? result.Stdout : result.Stdout[..separatorIndex];
        var labelsOutput = separatorIndex < 0 ? string.Empty : result.Stdout[(separatorIndex + separator.Length)..];
        var metadataByContainer = ParseComposeMetadataOutput(labelsOutput);

        return ParseDockerPsOutput(containersOutput)
            .Select(container => new DiscoveredContainerWithMetadata(
                container,
                metadataByContainer.GetValueOrDefault(container.ContainerName) ?? new ComposeMetadata()))
            .Select(item =>
            {
                item.Container.IsComposeManaged = item.Metadata.CanUpdate;
                return item;
            })
            .ToList();
    }

    private static Dictionary<string, ComposeMetadata> ParseComposeMetadataOutput(string output)
    {
        var result = new Dictionary<string, ComposeMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('\t');
            if (separatorIndex <= 0)
                continue;

            var containerName = line[..separatorIndex].Trim().TrimStart('/');
            var metadata = ParseComposeMetadata(line[(separatorIndex + 1)..]);
            result[containerName] = metadata;
        }

        return result;
    }

    private static ComposeMetadata ParseComposeMetadata(string labelsJson)
    {
        try
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(labelsJson) ?? [];
            return new ComposeMetadata
            {
                ServiceName = labels.GetValueOrDefault("com.docker.compose.service"),
                ConfigFiles = labels.GetValueOrDefault("com.docker.compose.project.config_files"),
                WorkingDirectory = labels.GetValueOrDefault("com.docker.compose.project.working_dir")
            };
        }
        catch (JsonException)
        {
            return new ComposeMetadata();
        }
    }

    private static List<DiscoveredRemoteContainerDto> ParseDockerPsOutput(string output)
    {
        var containers = new List<DiscoveredRemoteContainerDto>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                containers.Add(new DiscoveredRemoteContainerDto
                {
                    Id = root.GetPropertyOrDefault("ID"),
                    ContainerName = root.GetPropertyOrDefault("Names"),
                    Image = root.GetPropertyOrDefault("Image"),
                    State = root.GetPropertyOrDefault("State"),
                    Status = root.GetPropertyOrDefault("Status")
                });
            }
            catch (JsonException)
            {
                // Docker can return a non-JSON line only when its output is corrupted; ignore it.
            }
        }
        return containers.Where(x => !string.IsNullOrWhiteSpace(x.ContainerName)).ToList();
    }

    private static string BuildComposeUpdateCommand(RemoteContainer container)
    {
        var composeArguments = string.Join(" ", container.ComposeFiles!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => $"-f {EscapeShell(x)}"));
        var service = EscapeShell(container.ComposeServiceName!);
        return $"cd {EscapeShell(container.ComposeWorkingDirectory!)}" +
               $" && docker compose {composeArguments} pull {service}" +
               $" && docker compose {composeArguments} up --force-recreate -d {service}" +
               " && docker image prune -f";
    }

    private RemoteServer RequireServer(Guid serverId) => _db.Servers.FindById(serverId)
        ?? throw new KeyNotFoundException("Сервер не найден");

    private void EnsureServerNameIsAvailable(string name, Guid? exceptId)
    {
        var existing = _db.Servers.FindOne(x => x.Name == name.Trim());
        if (existing is not null && existing.Id != exceptId)
            throw new ArgumentException("Сервер с таким названием уже существует");
    }

    private static void ValidateServerRequest(SaveRemoteServerRequest request, bool passwordRequired)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("Название, host и пользователь обязательны");
        if (request.Port is < 1 or > 65535)
            throw new ArgumentException("SSH-порт должен быть от 1 до 65535");
        if (passwordRequired && string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("SSH-пароль обязателен");
    }

    private static void ThrowIfFailed(RemoteSshCommandResult result, string message)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Stderr) ? message : result.Stderr.Trim());
    }

    private static RemoteServerDto ToDto(RemoteServer server) => new(server.Id, server.Name, server.Host, server.Port,
        server.Username, !string.IsNullOrWhiteSpace(server.Password), server.CreatedAtUtc, server.UpdatedAtUtc);

    private static RemoteContainerStatusDto ToStatusDto(RemoteContainer container, string state, string status) => new()
    {
        Id = container.Id,
        ContainerName = container.ContainerName,
        ComposeServiceName = container.ComposeServiceName,
        CanUpdate = container.CanUpdate,
        State = state,
        Status = status
    };

    private static string EscapeShell(string value) => $"'{value.Replace("'", "'\\''")}'";
    private static string ActionLabel(string action) => action switch
    {
        "start" => "запущен",
        "stop" => "остановлен",
        "restart" => "перезапущен",
        "pull" => "обновлён",
        _ => "обработан"
    };
    private static ContainerActionResponseDto Ok(string message) => new() { Success = true, Message = message };
    private static ContainerActionResponseDto Fail(string message, string? details = null) => new() { Success = false, Message = message, ErrorDetails = details };

    private class ComposeMetadata
    {
        public string? ServiceName { get; init; }
        public string? ConfigFiles { get; init; }
        public string? WorkingDirectory { get; init; }
        public bool CanUpdate => !string.IsNullOrWhiteSpace(ServiceName)
            && !string.IsNullOrWhiteSpace(ConfigFiles)
            && !string.IsNullOrWhiteSpace(WorkingDirectory);
    }

    private record DiscoveredContainerWithMetadata(DiscoveredRemoteContainerDto Container, ComposeMetadata Metadata);
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() ?? string.Empty : string.Empty;
}
