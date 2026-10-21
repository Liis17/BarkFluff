using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Resolves Compose service names to this AdminPanel's actual container names and back.
/// Container discovery is scoped by the Compose project labels on the current AdminPanel.
/// </summary>
public sealed class DockerContainerNameResolver
{
    private const string ComposeProjectLabel = "com.docker.compose.project";
    private const string ComposeServiceLabel = "com.docker.compose.service";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private static readonly HashSet<string> ComposeServiceNames =
        PlatformServiceRegistry.All.Select(service => service.Container)
            .Append("admin-panel")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> LegacyContainerToService = CreateLegacyContainerMap();
    private static readonly IReadOnlyDictionary<string, string> LegacyServiceToContainer = CreateLegacyServiceMap();

    private readonly IDockerCommandRunner _commands;
    private readonly ILogger<DockerContainerNameResolver> _logger;
    private readonly string? _hostName;
    private readonly SemaphoreSlim _containersGate = new(1, 1);
    private readonly SemaphoreSlim _projectGate = new(1, 1);
    private readonly object _cacheLock = new();
    private IReadOnlyList<ContainerStatusDto>? _containers;
    private DateTime _containersExpiresAt;
    private string? _ownProject;
    private bool _ownProjectLoaded;
    private DateTime _projectExpiresAt;

    public DockerContainerNameResolver(
        IDockerCommandRunner commands,
        ILogger<DockerContainerNameResolver> logger)
        : this(commands, logger, Environment.GetEnvironmentVariable("HOSTNAME"))
    {
    }

    internal DockerContainerNameResolver(
        IDockerCommandRunner commands,
        ILogger<DockerContainerNameResolver> logger,
        string? hostName)
    {
        _commands = commands;
        _logger = logger;
        _hostName = hostName;
    }

    public async Task<string> ResolveContainerNameAsync(string serviceOrContainerName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = NormalizeName(serviceOrContainerName);
        if (input.Length == 0)
            return input;

        var containers = await GetContainersAsync(cancellationToken);
        var project = await GetOwnProjectAsync(cancellationToken);
        var exact = containers.FirstOrDefault(container =>
            string.Equals(NormalizeName(container.Name), input, StringComparison.OrdinalIgnoreCase));

        if (exact is not null && IsInProject(exact, project))
            return exact.Name;

        if (exact is not null && !string.IsNullOrWhiteSpace(exact.ComposeProject))
        {
            var localContainer = FindProjectContainer(containers, exact.ComposeService ?? "", project);
            if (localContainer is not null)
                return localContainer.Name;

            throw new InvalidOperationException($"Контейнер {input} не относится к Compose-проекту AdminPanel.");
        }

        var serviceName = ResolveServiceNameFromSnapshot(input, containers, project);
        if (serviceName is not null && ComposeServiceNames.Contains(serviceName))
        {
            var serviceContainer = FindProjectContainer(containers, serviceName, project);
            if (serviceContainer is not null)
                return serviceContainer.Name;

            if (LegacyServiceToContainer.TryGetValue(serviceName, out var legacyName))
                return legacyName;
        }

        if (input.StartsWith("barkfluff-", StringComparison.OrdinalIgnoreCase))
        {
            var prefixedService = input["barkfluff-".Length..];
            if (ComposeServiceNames.Contains(prefixedService) && LegacyServiceToContainer.TryGetValue(prefixedService, out var legacyName))
                return legacyName;
        }

        return input;
    }

    public async Task<string> ResolveServiceNameAsync(string containerOrServiceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = NormalizeName(containerOrServiceName);
        if (input.Length == 0 || ComposeServiceNames.Contains(input))
            return input;

        var containers = await GetContainersAsync(cancellationToken);
        var project = await GetOwnProjectAsync(cancellationToken);
        var exact = containers.FirstOrDefault(container =>
            string.Equals(NormalizeName(container.Name), input, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            if (IsInProject(exact, project) && !string.IsNullOrWhiteSpace(exact.ComposeService))
                return exact.ComposeService;

            if (!string.IsNullOrWhiteSpace(exact.ComposeProject))
                return input;
        }

        var serviceName = ResolveServiceNameFromSnapshot(input, containers, project);
        if (serviceName is not null)
            return serviceName;

        return LegacyContainerToService.TryGetValue(input, out var legacyService) ? legacyService : input;
    }

    /// <summary>
    /// Returns a service name only for a container in this panel's Compose project, or a legacy
    /// container without Compose labels. Containers labelled with another project are excluded.
    /// </summary>
    public async Task<string?> TryResolveComposeServiceNameAsync(string containerName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = NormalizeName(containerName);
        var containers = await GetContainersAsync(cancellationToken);
        var project = await GetOwnProjectAsync(cancellationToken);
        var exact = containers.FirstOrDefault(container =>
            string.Equals(NormalizeName(container.Name), input, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            if (!string.IsNullOrWhiteSpace(exact.ComposeProject))
                return IsInProject(exact, project) ? exact.ComposeService : null;

            if (LegacyContainerToService.TryGetValue(input, out var legacyService))
                return legacyService;

            return null;
        }

        return null;
    }

    internal async Task<IReadOnlyList<ContainerStatusDto>> GetContainersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (_containers is not null && DateTime.UtcNow < _containersExpiresAt)
                return _containers;
        }

        await _containersGate.WaitAsync(cancellationToken);
        try
        {
            lock (_cacheLock)
            {
                if (_containers is not null && DateTime.UtcNow < _containersExpiresAt)
                    return _containers;
            }

            var output = await _commands.RunAsync(cancellationToken, "ps", "--all", "--format", "json");
            var containers = ParseDockerPsOutput(output);
            lock (_cacheLock)
            {
                _containers = containers;
                _containersExpiresAt = DateTime.UtcNow.Add(CacheDuration);
            }
            return containers;
        }
        finally
        {
            _containersGate.Release();
        }
    }

    internal static List<ContainerStatusDto> ParseDockerPsOutput(string output)
    {
        var containers = new List<ContainerStatusDto>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line.Trim());
                var root = document.RootElement;
                var labels = root.TryGetProperty("Labels", out var labelsElement)
                    ? ParseComposeLabels(labelsElement.GetString())
                    : new Dictionary<string, string>(StringComparer.Ordinal);

                containers.Add(new ContainerStatusDto
                {
                    Id = GetString(root, "ID"),
                    Name = GetString(root, "Names"),
                    Image = GetString(root, "Image"),
                    State = GetString(root, "State"),
                    Status = GetString(root, "Status"),
                    Ports = root.TryGetProperty("Ports", out var ports) ? ports.GetString() ?? "none" : "none",
                    CreatedAt = DateTime.UtcNow,
                    ComposeProject = labels.GetValueOrDefault(ComposeProjectLabel),
                    ComposeService = labels.GetValueOrDefault(ComposeServiceLabel)
                });
            }
            catch (JsonException)
            {
                // A malformed docker ps row should not hide other containers.
            }
        }

        return containers;
    }

    internal static IReadOnlyDictionary<string, string> ParseComposeLabels(string? labels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(labels))
            return result;

        foreach (var label in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = label.IndexOf('=');
            if (separator <= 0)
                continue;

            result[label[..separator].Trim()] = label[(separator + 1)..].Trim();
        }

        return result;
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _containers = null;
            _ownProject = null;
            _ownProjectLoaded = false;
            _containersExpiresAt = DateTime.MinValue;
            _projectExpiresAt = DateTime.MinValue;
        }
    }

    private static string? ResolveServiceNameFromSnapshot(
        string input,
        IReadOnlyList<ContainerStatusDto> containers,
        string? project)
    {
        var prefixedService = input.StartsWith("barkfluff-", StringComparison.OrdinalIgnoreCase)
            ? input["barkfluff-".Length..]
            : null;

        if (prefixedService is not null && ComposeServiceNames.Contains(prefixedService))
        {
            var prefixedContainer = FindProjectContainer(containers, prefixedService, project);
            if (prefixedContainer is not null &&
                string.Equals(NormalizeName(prefixedContainer.Name), input, StringComparison.OrdinalIgnoreCase))
                return prefixedService;
        }

        var exact = containers.FirstOrDefault(container =>
            string.Equals(NormalizeName(container.Name), input, StringComparison.OrdinalIgnoreCase));
        if (exact is not null && IsInProject(exact, project) && !string.IsNullOrWhiteSpace(exact.ComposeService))
            return exact.ComposeService;

        if (ComposeServiceNames.Contains(input))
            return input;

        if (LegacyContainerToService.TryGetValue(input, out var legacyService))
            return legacyService;

        return null;
    }

    private async Task<string?> GetOwnProjectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (_ownProjectLoaded && DateTime.UtcNow < _projectExpiresAt)
                return _ownProject;
        }

        await _projectGate.WaitAsync(cancellationToken);
        try
        {
            lock (_cacheLock)
            {
                if (_ownProjectLoaded && DateTime.UtcNow < _projectExpiresAt)
                    return _ownProject;
            }

            var candidates = new[] { _hostName, "admin-panel" }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            string? project = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    var output = await _commands.RunAsync(
                        cancellationToken,
                        "inspect",
                        "--format",
                        "{{json .Config.Labels}}",
                        candidate!);
                    using var document = JsonDocument.Parse(output);
                    var labels = document.RootElement.ValueKind == JsonValueKind.Object
                        ? document.RootElement.EnumerateObject().ToDictionary(
                            property => property.Name,
                            property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "",
                            StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                    if (labels.TryGetValue(ComposeProjectLabel, out project) && !string.IsNullOrWhiteSpace(project))
                        break;
                    project = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Не удалось определить Compose-проект AdminPanel по контейнеру {ContainerName}", candidate);
                }
            }

            lock (_cacheLock)
            {
                _ownProject = project;
                _ownProjectLoaded = true;
                _projectExpiresAt = DateTime.UtcNow.Add(CacheDuration);
            }
            return project;
        }
        finally
        {
            _projectGate.Release();
        }
    }

    private static ContainerStatusDto? FindProjectContainer(
        IEnumerable<ContainerStatusDto> containers,
        string serviceName,
        string? project) =>
        project is null
            ? null
            : containers
                .Where(container => IsInProject(container, project) &&
                                    string.Equals(container.ComposeService, serviceName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(container => container.IsRunning)
                .ThenBy(container => container.Name, StringComparer.Ordinal)
                .FirstOrDefault();

    private static bool IsInProject(ContainerStatusDto container, string? project) =>
        project is not null && string.Equals(container.ComposeProject, project, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name) => name.Trim().TrimStart('/');

    private static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) ? property.GetString() ?? "" : "";

    private static IReadOnlyDictionary<string, string> CreateLegacyContainerMap()
    {
        var map = PlatformServiceRegistry.All.ToDictionary(
            service => service.Container,
            service => service.Container,
            StringComparer.OrdinalIgnoreCase);
        map["admin-panel"] = "admin-panel";
        map["postgres_barkfluff"] = "postgres";
        return map;
    }

    private static IReadOnlyDictionary<string, string> CreateLegacyServiceMap()
    {
        var map = PlatformServiceRegistry.All.ToDictionary(
            service => service.Container,
            service => service.Container,
            StringComparer.OrdinalIgnoreCase);
        map["admin-panel"] = "admin-panel";
        map["postgres"] = "postgres_barkfluff";
        return map;
    }
}
