namespace Barkfluff.AdminPanel.Models.Dtos;

/// <summary>
/// Снимок состояния платформы, собирается HealthCollectorService в фоне:
/// liveness (/health/live с fallback /ping), readiness (/health/ready),
/// docker-статусы, инфраструктура.
/// </summary>
public sealed record HealthSnapshot(
    DateTime GeneratedAtUtc,
    IReadOnlyList<ServiceHealthStatus> Services,
    HealthSummary Summary,
    string SystemStatus,
    IReadOnlyList<InfrastructureHealth> Infrastructure);

public sealed record HealthSummary(int Total, int Healthy, int Degraded, int Down, int Unknown);

/// <summary>
/// Статус одного сервиса: healthy | degraded | down | unknown.
/// degraded = жив, но readiness частично провален; down = мёртв или все зависимости недоступны.
/// </summary>
public sealed record ServiceHealthStatus(
    string Name,
    string Container,
    string Status,
    bool HasProbe,
    bool? ProbeUp,
    int? ProbeLatencyMs,
    string? DockerState,
    string? LastSeenUtc,
    ReadinessHealth? Readiness);

public sealed record ReadinessHealth(string Status, IReadOnlyList<DependencyCheckDto> Checks);

public sealed record DependencyCheckDto(string Name, string Status, long? LatencyMs, string? Error);

public sealed record InfrastructureHealth(string Name, string Status, string Source, string? Detail);
