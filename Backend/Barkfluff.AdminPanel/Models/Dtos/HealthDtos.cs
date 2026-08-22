namespace Barkfluff.AdminPanel.Models.Dtos;

/// <summary>
/// Снимок liveness-состояния платформы, собирается HealthCollectorService в фоне.
/// </summary>
public sealed record HealthSnapshot(
    DateTime GeneratedAtUtc,
    IReadOnlyList<ServiceHealthStatus> Services,
    HealthSummary Summary,
    string SystemStatus);

public sealed record HealthSummary(int Total, int Healthy, int Degraded, int Down, int Unknown);

/// <summary>
/// Статус одного сервиса: healthy | degraded | down | unknown.
/// </summary>
public sealed record ServiceHealthStatus(
    string Name,
    string Container,
    string Status,
    bool HasProbe,
    bool? ProbeUp,
    int? ProbeLatencyMs,
    string? DockerState,
    string? LastSeenUtc);
