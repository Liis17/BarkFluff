namespace BarkFluff.GrpcServer;

/// <summary>Optional service-specific readiness check collected with built-in dependencies.</summary>
public interface IBarkFluffReadinessContributor
{
    Task<DependencyCheck> CheckAsync(CancellationToken cancellationToken = default);
}
