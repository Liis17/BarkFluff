namespace BarkFluff.GrpcServer.Tracker;

public class RequestContext
{
    public string? OperationSystem { get; init; }

    public string? IpAddress { get; init; }

    /// <summary>
    /// Адрес, полученный от reverse proxy или TCP-соединения.
    /// В отличие от IpAddress не использует клиентские gRPC-метаданные.
    /// </summary>
    public string? TrustedIpAddress { get; init; }

    public string? DeviceName { get; init; }

    public string? AppName { get; init; }

    public string? AppVersion { get; init; }

    public string? DeviceId { get; init; }
}
